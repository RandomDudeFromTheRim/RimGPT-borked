using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using Verse.Sound;

namespace RimGPT
{
	public struct TTSResponse
	{
		public int Error;
		public string Speaker;
		public int Cached;
		public string Text;
		public string tasktype;
		public string URL;
		public string MP3;
	}

	public class TTS
	{
		public const string LocalAiBaseUrl = "http://192.168.1.79:8080";
		public const string LocalAiTtsModel = "voice-en-GB-cori-high";
		public static string LocalAiSpeechEndpoint => $"{LocalAiBaseUrl}/v1/audio/speech";

		private static AudioSource audioSource = null;
		private static readonly object audioSourceLock = new();

		public static AudioSource GetAudioSource()
		{
			lock (audioSourceLock)
			{
				if (audioSource == null)
				{
					var gameObject = new GameObject("HarmonyOneShotSourcesWorldContainer");
					UnityEngine.Object.DontDestroyOnLoad(gameObject);
					gameObject.transform.position = Vector3.zero;
					var gameObject2 = new GameObject("HarmonyOneShotSource");
					gameObject2.transform.parent = gameObject.transform;
					gameObject2.transform.localPosition = Vector3.zero;
					audioSource = AudioSourceMaker.NewAudioSourceOn(gameObject2);
					audioSource.spatialBlend = 0f;
					audioSource.rolloffMode = AudioRolloffMode.Linear;
					audioSource.minDistance = 100000;
					audioSource.bypassEffects = true;
					audioSource.bypassListenerEffects = true;
					audioSource.bypassReverbZones = true;
					audioSource.ignoreListenerPause = true;
					audioSource.ignoreListenerVolume = true;
					audioSource.volume = 1;
				}
				return audioSource;
			}
		}

		public static async Task<AudioClip> AudioClipFromLocalAi(Persona persona, string text, Action<string> errorCallback)
		{
			var payload = new
			{
				model = LocalAiTtsModel,
				input = text,
				backend = "piper",
				response_format = "wav"
			};
			var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
			using var request = new UnityWebRequest(LocalAiSpeechEndpoint, "POST");
			request.uploadHandler = new UploadHandlerRaw(body);
			request.downloadHandler = new DownloadHandlerAudioClip(LocalAiSpeechEndpoint, AudioType.WAV);
			request.SetRequestHeader("Content-Type", "application/json");
			try
			{
				var asyncOperation = request.SendWebRequest();
				while (!asyncOperation.isDone && RimGPTMod.Running)
					await Task.Delay(200);
				RimGPTMod.Settings.charactersSentTts += text.Length;
			}
			catch (Exception exception)
			{
				var error = $"Error communicating with LocalAI: {exception}";
				errorCallback?.Invoke(error);
				return default;
			}
			var code = request.responseCode;
			if (Tools.DEBUG)
				Logger.Warning($"LocalAI => {code} {request.error}");
			if (code >= 300)
			{
				var error = $"Got {code} response from LocalAI: {request.error}";
				errorCallback?.Invoke(error);
				return default;
			}
			return await Main.Perform(() => DownloadHandlerAudioClip.GetContent(request));
		}

		//public static async Task PlayTTSMP3(string text, string voice = "Salli", string source = "ttsmp3")
		//{
		//		var form = new WWWForm();
		//		form.AddField("msg", text);
		//		form.AddField("lang", voice);
		//		form.AddField("source", source);
		//		var response = await DispatchFormPost<TTSResponse>("https://ttsmp3.com/makemp3_new.php", form);
		//		var audioClip = await DownloadAudioClip(response.URL);
		//		GetAudioSource().PlayOneShot(audioClip);
		//}

		public static void TestKey(Persona persona, Action callback)
		{
			Tools.SafeAsync(async () =>
			{
				var text = "This is a test message";
				string error = null;
				if (RimGPTMod.Settings.IsConfigured)
				{
					var prompt = "Say something random.";
					if (persona.personalityLanguage != "-")
						prompt += $" Your response must be in {persona.personalityLanguage}.";
					var dummyAI = new AI();
					var result = await dummyAI.SimplePrompt(prompt);
					text = result.Item1;
					error = result.Item2;
				}
				if (text != null)
				{
					var audioClip = await AudioClipFromLocalAi(persona, text, e => error = e);
					if (audioClip != null)
					{
						var source = GetAudioSource();
						source.Stop();
						source.clip = audioClip;
						source.volume = RimGPTMod.Settings.speechVolume;
						source.Play();
					}
				}
				if (error != null)
					LongEventHandler.ExecuteWhenFinished(() =>
					{
						var dialog = new Dialog_MessageBox(error, null, null, null, null, null, false, callback, callback);
						Find.WindowStack.Add(dialog);
					});
				else
					callback?.Invoke();
			});
		}
	}
}
