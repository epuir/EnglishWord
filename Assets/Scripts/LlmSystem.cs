using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.Networking;
using System.Text;

// 大模型提供者接口，便于扩展不同API
public interface ILlmProvider
{
    string ModelName { get; }
    Task<string> SendRequestAsync(string prompt);
    void SetConfig(Dictionary<string, string> config);
}

// 阿里通义千问大模型 API Provider 示例（兼容 OpenAI chat/completions 风格）
public class AliQwenProvider : ILlmProvider
{
    public string ModelName => "AliQwen";

    private string _apiKey = string.Empty; // 从 LlmSystem.Config["aliApiKey"] 读取
    private string _apiUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
    private string _model = "qwen-plus"; // 确保默认就是 qwen-plus

    [System.Serializable]
    private class ChatMessage
    {
        public string role;
        public string content;
    }

    [System.Serializable]
    private class ChatRequestBody
    {
        public string model;
        public ChatMessage[] messages;
        public float temperature = 0.8f;
        public float top_p = 0.8f;
        public int max_tokens = 512;
        public bool stream = false;
    }

    public void SetConfig(Dictionary<string, string> config)
    {
        if (config == null) return;

        if (config.TryGetValue("aliApiKey", out var apiKey))
            _apiKey = apiKey?.Trim(); // 去掉首尾空白，避免 Header 非法字符

        if (config.TryGetValue("aliModel", out var model) && !string.IsNullOrWhiteSpace(model))
            _model = model;

        if (config.TryGetValue("aliApiUrl", out var apiUrl) && !string.IsNullOrWhiteSpace(apiUrl))
            _apiUrl = apiUrl;

        Debug.Log($"[AliQwenProvider] Config applied -> model: {_model}, apiUrl: {_apiUrl}, hasApiKey: {!string.IsNullOrEmpty(_apiKey)}");
    }

    public async Task<string> SendRequestAsync(string prompt)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new System.InvalidOperationException("AliQwen API Key 未配置，请在 LlmSystem Inspector 中设置 aliApiKey 或通过 SetConfig 传入 aliApiKey");

        string json = BuildChatCompletionsJson(prompt);
        Debug.Log($"[AliQwenProvider] Request JSON: {json}");
        Debug.Log($"[AliQwenProvider] Using ApiUrl: {_apiUrl}");

        using (var req = new UnityWebRequest(_apiUrl, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            var op = req.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }

#if UNITY_2020_2_OR_NEWER
            if (req.result == UnityWebRequest.Result.Success)
#else
            if (!req.isNetworkError && !req.isHttpError)
#endif
            {
                Debug.Log($"[AliQwenProvider] Response JSON: {req.downloadHandler.text}");
                return req.downloadHandler.text; // 返回通义千问原始 JSON
            }

            Debug.LogError($"[AliQwenProvider] HTTP Error {req.responseCode}: {req.error}\\nBody: {req.downloadHandler.text}");
            throw new System.Exception($"AliQwen HTTP {req.responseCode}: {req.error}");
        }
    }

    // 按千问官网示例构造 JSON：model + messages[system,user]
    private string BuildChatCompletionsJson(string prompt)
    {
        var body = new ChatRequestBody
        {
            model = _model,
            messages = new[]
            {
                new ChatMessage
                {
                    role = "system",
                    content = "You are a helpful assistant."
                },
                new ChatMessage
                {
                    role = "user",
                    content = prompt
                }
            },
            temperature = 0.8f,
            top_p = 0.8f,
            max_tokens = 512,
            stream = false
        };

        string json = JsonUtility.ToJson(body);
        return json;
    }
}

public class LlmSystem : MonoBehaviour
{
    // 单例实例，方便全局访问
    public static LlmSystem Instance { get; private set; }
    private readonly Dictionary<string, ILlmProvider> _providers = new Dictionary<string, ILlmProvider>();

    [Header("LLM 配置")]
    [Tooltip("当前使用的模型提供者名称，例如: AliQwen")]
    public string currentModel = "AliQwen";

    [Tooltip("默认千问模型名称，例如: qwen-plus")]
    public string defaultModel = "qwen-plus";

    [Header("通义千问凭证")]
    [Tooltip("通义千问 API Key，将在运行时自动注入到 Provider 中。请勿提交到版本库。")]
    [SerializeField]
    private string aliApiKey = ""; // 在 Inspector 中可视配置

    // 公共配置（例如 API Key、模型名、API 地址等）
    public Dictionary<string, string> Config { get; } = new Dictionary<string, string>();

    // 简单日志记录（如需可扩展为写入文件或自定义日志系统）
    private readonly List<string> _logs = new List<string>();

    private void Awake()
    {
        // 单例初始化
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("检测到重复的 LlmSystem 实例，销毁后创建的这个。请确保场景中只保留一个 LlmSystem。");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 将 Inspector 中配置的 aliApiKey 写入 Config，方便 Provider 统一读取
        if (!string.IsNullOrWhiteSpace(aliApiKey))
        {
            Config["aliApiKey"] = aliApiKey;
        }

        // 只注册阿里千问 Provider
        RegisterProvider(new AliQwenProvider());

        // 默认使用 qwen-plus 作为模型
        if (!Config.ContainsKey("aliModel"))
            Config["aliModel"] = defaultModel;

        if (!Config.ContainsKey("aliApiUrl"))
            Config["aliApiUrl"] = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";

        // 将配置同步到所有Provider
        SyncConfigToProviders();
    }

    // 注册模型提供者
    public void RegisterProvider(ILlmProvider provider)
    {
        if (provider == null)
        {
            LogError("尝试注册的 Provider 为空");
            return;
        }

        _providers[provider.ModelName] = provider;
        provider.SetConfig(Config);
        Log($"已注册模型提供者: {provider.ModelName}");
    }

    // 切换当前模型
    public void SetCurrentModel(string modelName)
    {
        if (_providers.ContainsKey(modelName))
        {
            currentModel = modelName;
            Log($"当前模型已切换为: {modelName}");
        }
        else
        {
            LogError($"模型 {modelName} 未注册");
        }
    }

    // 发送请求到当前模型
    public async Task<string> SendRequestAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            LogError("发送请求失败: prompt 为空");
            return null;
        }

        if (!_providers.TryGetValue(currentModel, out var provider))
        {
            LogError("未设置有效模型或模型未注册");
            return null;
        }

        try
        {
            var response = await provider.SendRequestAsync(prompt);
            Log($"请求: {prompt}\n回复: {response}");
            return response;
        }
        catch (System.Exception ex)
        {
            LogError($"请求失败: {ex.Message}");
            return null;
        }
    }

    // 更新配置并同步到所有Provider
    public void SetConfig(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;

        Config[key] = value;
        SyncConfigToProviders();
        Log($"配置已更新: {key} = {value}");
    }

    private void SyncConfigToProviders()
    {
        foreach (var provider in _providers.Values)
        {
            provider.SetConfig(Config);
        }
    }

    // 对外提供只读日志访问
    public IReadOnlyList<string> GetLogs() => _logs.AsReadOnly();

    // 日志与错误处理
    private void Log(string msg)
    {
        _logs.Add(msg);
        Debug.Log(msg);
    }

    private void LogError(string msg)
    {
        _logs.Add("ERROR: " + msg);
        Debug.LogError(msg);
    }
}


