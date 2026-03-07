using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 使用 LlmSystem 的简单示例：
/// 1. 启动时配置 API Key / 模型
/// 2. 调用大模型获取回答，并在控制台输出
/// </summary>
public class LlmUsageExample : MonoBehaviour
{
    [TextArea]
    [Tooltip("启动时发送给通义千问的提示词（Prompt）")]
    public string prompt = "请用一句话介绍你自己。";

    [Tooltip("是否在 Start 时自动发送一次请求")] 
    public bool autoSendOnStart = true;

    private async void Start()
    {
        if (!autoSendOnStart)
            return;

        await SendExampleRequest();
    }

    [ContextMenu("发送示例请求到通义千问")]
    public async void SendExampleRequestContextMenu()
    {
        await SendExampleRequest();
    }

    private async Task SendExampleRequest()
    {
        if (LlmSystem.Instance == null)
        {
            Debug.LogError("[LlmUsageExample] LlmSystem.Instance 为空，请确认场景中存在 LlmSystem 单例对象。");
            return;
        }
        Debug.Log("[LlmUsageExample] 正在向通义千问发送请求...");
        var responseJson = await LlmSystem.Instance.SendRequestAsync(prompt);
        if (responseJson == null)
        {
            Debug.LogError("[LlmUsageExample] 请求失败或返回为空，请查看 LlmSystem 日志。");
            return;
        }
        Debug.Log("[LlmUsageExample] 通义千问原始响应 JSON:\n" + responseJson);
    }
}
