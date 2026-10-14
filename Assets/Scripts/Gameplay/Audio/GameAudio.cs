using System.Collections.Generic;
using UnityEngine;

namespace SplatoonC.Gameplay.Audio
{
    // 全域聲音入口(靜態,不需要場景物件):
    //   Play(id, 位置)   3D 音效;超出可聽距離直接略過
    //   Play2D(id)       自己的動作、UI、對戰流程
    //   PlayFrom(本機?, id, 位置)  本機角色發出的聲音走 2D、其他角色走 3D
    //   StartLoop(id, 跟隨物)  回傳 AudioLoop,Stop() 淡出
    //   PlayMusic(id, 淡入秒, 起始秒) / StopMusic
    // id 可以是變體組名(shoot_shooter → shoot_shooter / _2 / _3 / _4 隨機)。
    // 同組有同時發聲上限與最短間隔(8 人同時連射也不會糊成一團);音量 = 清單建議值 × 呼叫值 × 類別 × 主音量。
    public static class GameAudio
    {
        public const float MaxAudibleDistance = 48f;

        private const string PrefMaster = "SplatoonC.Audio.Master";
        private const string PrefMusic = "SplatoonC.Audio.Music";
        private const string PrefSfx = "SplatoonC.Audio.Sfx";

        private static readonly Dictionary<string, List<AudioLibrary.Entry>> Groups =
            new Dictionary<string, List<AudioLibrary.Entry>>();
        private static readonly Dictionary<string, AudioLibrary.Entry> ById =
            new Dictionary<string, AudioLibrary.Entry>();

        private static bool _loaded;
        private static bool _prefsLoaded;
        private static float _master = 1f;
        private static float _music = 0.8f;
        private static float _sfx = 1f;
        private static GameAudioHost _host;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Groups.Clear();
            ById.Clear();
            _loaded = false;
            _host = null;
        }

        public static float MasterVolume
        {
            get { LoadPrefs(); return _master; }
            set { LoadPrefs(); _master = Mathf.Clamp01(value); PlayerPrefs.SetFloat(PrefMaster, _master); }
        }

        public static float MusicVolume
        {
            get { LoadPrefs(); return _music; }
            set { LoadPrefs(); _music = Mathf.Clamp01(value); PlayerPrefs.SetFloat(PrefMusic, _music); }
        }

        public static float SfxVolume
        {
            get { LoadPrefs(); return _sfx; }
            set { LoadPrefs(); _sfx = Mathf.Clamp01(value); PlayerPrefs.SetFloat(PrefSfx, _sfx); }
        }

        // 最近一次成功播放的音效 id 與累計次數;Played 每次實際發聲(單發或循環開始)時觸發(測試/除錯用)
        public static string LastPlayedId { get; private set; }
        public static int PlayCount { get; private set; }
        public static event System.Action<string> Played;

        internal static void NotifyPlayed(string id)
        {
            LastPlayedId = id;
            PlayCount++;
            Played?.Invoke(id);
        }

        public static bool Has(string id)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(id) && (Groups.ContainsKey(id) || ById.ContainsKey(id));
        }

        public static void Play(string id, Vector3 position, float volume = 1f, float pitchJitter = 0.04f)
        {
            if (TryResolve(id, out AudioLibrary.Entry entry) && Host != null)
            {
                Host.PlayOneShot(entry, true, position, volume, Jitter(pitchJitter));
            }
        }

        public static void Play2D(string id, float volume = 1f, float pitchJitter = 0f)
        {
            if (TryResolve(id, out AudioLibrary.Entry entry) && Host != null)
            {
                Host.PlayOneShot(entry, false, Vector3.zero, volume, Jitter(pitchJitter));
            }
        }

        public static void PlayFrom(bool local, string id, Vector3 position, float volume = 1f, float pitchJitter = 0.04f)
        {
            if (local)
            {
                Play2D(id, volume, pitchJitter);
            }
            else
            {
                Play(id, position, volume, pitchJitter);
            }
        }

        public static AudioLoop StartLoop(string id, Transform follow, bool spatial = true, float volume = 1f)
        {
            if (!TryResolve(id, out AudioLibrary.Entry entry) || Host == null)
            {
                return null;
            }
            return Host.StartLoop(entry, follow, spatial, volume);
        }

        public static void PlayMusic(string id, float fadeSeconds = 0.5f, float startAtSeconds = 0f)
        {
            if (TryResolve(id, out AudioLibrary.Entry entry) && Host != null)
            {
                Host.PlayMusic(entry, fadeSeconds, startAtSeconds);
            }
        }

        public static void StopMusic(float fadeSeconds = 0.5f)
        {
            if (_host != null)
            {
                _host.StopMusic(fadeSeconds);
            }
        }

        public static string CurrentMusicId => _host != null ? _host.CurrentMusicId : null;

        private static float Jitter(float amount)
        {
            return amount > 0f ? 1f + Random.Range(-amount, amount) : 1f;
        }

        private static GameAudioHost Host
        {
            get
            {
                if (!Application.isPlaying)
                {
                    return null;
                }
                if (_host == null)
                {
                    var go = new GameObject("GameAudio");
                    Object.DontDestroyOnLoad(go);
                    _host = go.AddComponent<GameAudioHost>();
                }
                return _host;
            }
        }

        internal static float SfxGain => MasterVolume * SfxVolume;
        internal static float MusicGain => MasterVolume * MusicVolume;

        private static void LoadPrefs()
        {
            if (_prefsLoaded)
            {
                return;
            }
            _prefsLoaded = true;
            _master = PlayerPrefs.GetFloat(PrefMaster, 1f);
            _music = PlayerPrefs.GetFloat(PrefMusic, 0.8f);
            _sfx = PlayerPrefs.GetFloat(PrefSfx, 1f);
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            var library = Resources.Load<AudioLibrary>(AudioLibrary.ResourcePath);
            if (library == null)
            {
                Debug.LogWarning("GameAudio:找不到 Resources/AudioLibrary(先跑 Tools/SplatoonC/Audio Setup),整場靜音");
                return;
            }
            foreach (AudioLibrary.Entry entry in library.Entries)
            {
                if (entry.Clip == null || string.IsNullOrEmpty(entry.Id))
                {
                    continue;
                }
                ById[entry.Id] = entry;
                string group = string.IsNullOrEmpty(entry.Group) ? AudioLibrary.GroupOf(entry.Id) : entry.Group;
                if (!Groups.TryGetValue(group, out List<AudioLibrary.Entry> list))
                {
                    list = new List<AudioLibrary.Entry>();
                    Groups[group] = list;
                }
                list.Add(entry);
            }
        }

        private static bool TryResolve(string id, out AudioLibrary.Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(id) || !Application.isPlaying)
            {
                return false;
            }
            EnsureLoaded();
            if (Groups.TryGetValue(id, out List<AudioLibrary.Entry> list) && list.Count > 0)
            {
                entry = list.Count == 1 ? list[0] : list[Random.Range(0, list.Count)];
                return true;
            }
            return ById.TryGetValue(id, out entry);
        }
    }

    // 循環音效的控制把手(游動、爬牆、龍捲風…)
    public sealed class AudioLoop
    {
        internal AudioSource Source;
        internal Transform Follow;
        internal float BaseVolume;
        internal float Level;
        internal float TargetLevel = 1f;
        internal float FadeSpeed = 8f;
        internal bool Stopping;

        public bool IsAlive => Source != null && !Stopping;

        // 相對音量(0~1,平滑過渡)
        public void SetVolume(float relative)
        {
            if (!Stopping)
            {
                TargetLevel = Mathf.Clamp01(relative);
            }
        }

        public void Stop(float fadeSeconds = 0.15f)
        {
            Stopping = true;
            TargetLevel = 0f;
            FadeSpeed = fadeSeconds > 0.001f ? 1f / fadeSeconds : 1000f;
        }
    }

    // 執行期聲音宿主(跨場景常駐):單發音效聲部池、循環音效、兩軌交叉淡入淡出的音樂
    [DefaultExecutionOrder(1000)]
    internal sealed class GameAudioHost : MonoBehaviour
    {
        private const int VoiceCount = 40;
        private const float DefaultMinInterval = 0.015f;
        private const int DefaultMaxPerGroup = 4;

        // 同組同時發聲上限與最短間隔(秒)
        private static readonly Dictionary<string, Vector2> GroupLimits = new Dictionary<string, Vector2>
        {
            { "shoot_shooter", new Vector2(6, 0.02f) },
            { "ink_hit_floor", new Vector2(4, 0.05f) },
            { "ink_hit_wall", new Vector2(3, 0.06f) },
            { "ink_drip", new Vector2(2, 0.1f) },
            { "footstep", new Vector2(6, 0.03f) },
            { "step_enemy_ink", new Vector2(3, 0.1f) },
            { "hit_marker", new Vector2(2, 0.05f) },
            { "bomb_fuse_beep", new Vector2(4, 0.03f) },
            { "bomb_bounce", new Vector2(3, 0.05f) },
            { "low_ink", new Vector2(1, 0.6f) },
            { "player_hurt", new Vector2(1, 0.25f) },
        };

        private AudioSource[] _voices;
        private string[] _voiceGroups;
        private float[] _voiceStart;
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private readonly List<AudioLoop> _loops = new List<AudioLoop>();
        private readonly Stack<AudioSource> _idleLoopSources = new Stack<AudioSource>();

        private AudioSource[] _music;
        private float[] _musicLevel;
        private float[] _musicTarget;
        private float[] _musicBase;
        private string[] _musicIds;
        private int _currentMusic = -1;
        private float _musicFadeSpeed = 2f;

        private AudioListener _listener;
        private float _nextListenerSearch;

        public string CurrentMusicId =>
            _currentMusic >= 0 && _music[_currentMusic].isPlaying ? _musicIds[_currentMusic] : null;

        private void Awake()
        {
            _voices = new AudioSource[VoiceCount];
            _voiceGroups = new string[VoiceCount];
            _voiceStart = new float[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                _voices[i] = CreateSource("Voice" + i);
            }
            _music = new AudioSource[2];
            _musicLevel = new float[2];
            _musicTarget = new float[2];
            _musicBase = new float[2];
            _musicIds = new string[2];
            for (int i = 0; i < 2; i++)
            {
                AudioSource source = CreateSource("Music" + i);
                source.spatialBlend = 0f;
                source.priority = 0;
                source.ignoreListenerPause = true;
                _music[i] = source;
            }
        }

        private AudioSource CreateSource(string sourceName)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 4f;
            source.maxDistance = GameAudio.MaxAudibleDistance;
            return source;
        }

        private Vector3? ListenerPosition
        {
            get
            {
                if (_listener == null || !_listener.isActiveAndEnabled)
                {
                    if (Time.unscaledTime < _nextListenerSearch)
                    {
                        return null;
                    }
                    _nextListenerSearch = Time.unscaledTime + 1f;
                    _listener = FindAnyObjectByType<AudioListener>();
                    if (_listener == null)
                    {
                        return null;
                    }
                }
                return _listener.transform.position;
            }
        }

        public void PlayOneShot(in AudioLibrary.Entry entry, bool spatial, Vector3 position, float volume, float pitch)
        {
            if (spatial)
            {
                Vector3? listener = ListenerPosition;
                if (listener.HasValue && (listener.Value - position).sqrMagnitude
                    > GameAudio.MaxAudibleDistance * GameAudio.MaxAudibleDistance)
                {
                    return;
                }
            }
            string group = string.IsNullOrEmpty(entry.Group) ? entry.Id : entry.Group;
            Vector2 limit = GroupLimits.TryGetValue(group, out Vector2 custom)
                ? custom
                : new Vector2(DefaultMaxPerGroup, DefaultMinInterval);
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(group, out float last) && now - last < limit.y)
            {
                return;
            }
            int sameGroup = 0;
            int free = -1;
            int oldest = 0;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].isPlaying)
                {
                    if (_voiceGroups[i] == group)
                    {
                        sameGroup++;
                    }
                    if (_voiceStart[i] < _voiceStart[oldest])
                    {
                        oldest = i;
                    }
                }
                else if (free < 0)
                {
                    free = i;
                }
            }
            if (sameGroup >= (int)limit.x)
            {
                return;
            }
            int index = free >= 0 ? free : oldest;
            AudioSource source = _voices[index];
            source.Stop();
            source.clip = entry.Clip;
            source.loop = false;
            source.volume = Mathf.Clamp01(entry.Volume * volume * GameAudio.SfxGain);
            source.pitch = pitch;
            source.spatialBlend = spatial ? 1f : 0f;
            // 暫停選單開著(AudioListener.pause)時觸發的 2D 音效就是選單 UI 音:不受暫停影響,
            // 否則會被壓住,等解除暫停才一起冒出來
            source.ignoreListenerPause = !spatial && AudioListener.pause;
            source.transform.position = position;
            source.Play();
            _voiceGroups[index] = group;
            _voiceStart[index] = now;
            _lastPlayed[group] = now;
            GameAudio.NotifyPlayed(entry.Id);
        }

        public AudioLoop StartLoop(in AudioLibrary.Entry entry, Transform follow, bool spatial, float volume)
        {
            AudioSource source = _idleLoopSources.Count > 0 ? _idleLoopSources.Pop() : CreateSource("Loop");
            source.clip = entry.Clip;
            source.loop = true;
            source.pitch = 1f;
            source.spatialBlend = spatial ? 1f : 0f;
            source.volume = 0f;
            if (follow != null)
            {
                source.transform.position = follow.position;
            }
            // 從隨機位置開始:多個同樣的循環不會同相位疊在一起
            source.time = entry.Clip.length > 0.1f ? Random.Range(0f, entry.Clip.length * 0.9f) : 0f;
            source.Play();
            var loop = new AudioLoop
            {
                Source = source,
                Follow = follow,
                BaseVolume = entry.Volume,
                Level = 0f,
                TargetLevel = Mathf.Clamp01(volume),
            };
            _loops.Add(loop);
            GameAudio.NotifyPlayed(entry.Id);
            return loop;
        }

        public void PlayMusic(in AudioLibrary.Entry entry, float fadeSeconds, float startAt)
        {
            if (_currentMusic >= 0 && _musicIds[_currentMusic] == entry.Id && _music[_currentMusic].isPlaying
                && startAt <= 0f)
            {
                return;
            }
            int next = _currentMusic == 0 ? 1 : 0;
            if (_currentMusic >= 0)
            {
                _musicTarget[_currentMusic] = 0f;
            }
            AudioSource source = _music[next];
            source.Stop();
            source.clip = entry.Clip;
            source.loop = entry.Loop;
            source.time = Mathf.Clamp(startAt, 0f, Mathf.Max(0f, entry.Clip.length - 0.05f));
            source.Play();
            _musicBase[next] = entry.Volume;
            _musicIds[next] = entry.Id;
            _musicLevel[next] = fadeSeconds > 0.001f ? 0f : 1f;
            _musicTarget[next] = 1f;
            _musicFadeSpeed = fadeSeconds > 0.001f ? 1f / fadeSeconds : 1000f;
            _currentMusic = next;
            ApplyMusicVolume(next);
        }

        public void StopMusic(float fadeSeconds)
        {
            _musicFadeSpeed = fadeSeconds > 0.001f ? 1f / fadeSeconds : 1000f;
            for (int i = 0; i < _music.Length; i++)
            {
                _musicTarget[i] = 0f;
            }
            _currentMusic = -1;
        }

        private void ApplyMusicVolume(int i)
        {
            _music[i].volume = Mathf.Clamp01(_musicLevel[i] * _musicBase[i] * GameAudio.MusicGain);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _music.Length; i++)
            {
                _musicLevel[i] = Mathf.MoveTowards(_musicLevel[i], _musicTarget[i], _musicFadeSpeed * dt);
                ApplyMusicVolume(i);
                if (_musicLevel[i] <= 0f && _musicTarget[i] <= 0f && _music[i].isPlaying)
                {
                    _music[i].Stop();
                }
            }

            float gain = GameAudio.SfxGain;
            for (int i = _loops.Count - 1; i >= 0; i--)
            {
                AudioLoop loop = _loops[i];
                bool followGone = loop.Follow == null || !loop.Follow.gameObject.activeInHierarchy;
                if (followGone && !loop.Stopping)
                {
                    loop.Stop(0.1f);
                }
                if (!followGone)
                {
                    loop.Source.transform.position = loop.Follow.position;
                }
                loop.Level = Mathf.MoveTowards(loop.Level, loop.TargetLevel, loop.FadeSpeed * dt);
                loop.Source.volume = Mathf.Clamp01(loop.Level * loop.BaseVolume * gain);
                if (loop.Stopping && loop.Level <= 0f)
                {
                    loop.Source.Stop();
                    loop.Source.clip = null;
                    _idleLoopSources.Push(loop.Source);
                    loop.Source = null;
                    _loops.RemoveAt(i);
                }
            }
        }
    }
}
