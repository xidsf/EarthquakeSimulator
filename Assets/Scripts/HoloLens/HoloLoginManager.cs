using UnityEngine;
using UnityEngine.SceneManagement;

// HoloLens 2 전용 로그인 매니저
// Login.unity 씬의 로그인 버튼 OnClick에 이 컴포넌트를 연결할 것
public class HoloLoginManager : MonoBehaviour
{
    // 로그인 버튼 클릭 시 MainScene으로 이동
    public void OnLoginButtonClicked()
    {
        SceneManager.LoadScene("MainScene");
    }
}
