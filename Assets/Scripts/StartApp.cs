using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class StartApp : MonoBehaviour
{
    public Button startButton;
    public Text statusText;
    void Start()
    {
        startButton.onClick.AddListener(OnStartButtonClicked);
        statusText.text = "AI背单词";
    }

    private void OnStartButtonClicked()
    {
        
    }
}
