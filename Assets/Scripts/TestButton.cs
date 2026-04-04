using UnityEngine;

public class ButtonTester : MonoBehaviour
{
    // 1. 단순 확인용 함수
    public void OnSimpleButtonClicked()
    {
        Debug.Log(" 버튼이 정상적으로 클릭되었습니다!");
    }

    // 2. 여러 버튼을 테스트할 때 헷갈리지 않게 이름을 넘겨받는 함수
    public void OnSpecificButtonClicked(string buttonName)
    {
        Debug.Log($" [{buttonName}] 버튼 클릭 성공!");
    }
}