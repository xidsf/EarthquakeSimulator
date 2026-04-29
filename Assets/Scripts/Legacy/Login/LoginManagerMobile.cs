using UnityEngine;
using UnityEngine.SceneManagement;

public class LoginManagerMobile : MonoBehaviour
{
    // 버튼의 OnClick 이벤트에 연결
    public void OnLoginButtonClicked()
    {
        SceneManager.LoadScene("RoomBuildScene");
    }
}
