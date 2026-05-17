using UnityEngine;
using TMPro;
using MixedReality.Toolkit.UX;
using UnityEngine.Events;

public class NumpadController : MonoBehaviour
{
    [Header("Numpad Buttons")]
    [Tooltip("0부터 9까지 순서대로 버튼을 넣어주세요")]
    public PressableButton[] numberButtons = new PressableButton[10]; // 0~9 버튼 배열
    public PressableButton btn_Backspace; // 지우기 버튼
    public PressableButton btn_Done;      // 완료 버튼

    // 현재 숫자를 입력받을 대상 텍스트
    private TMP_Text targetText;
    private UnityAction closeRequested;

    // 첫 입력인지 확인하는 변수
    private bool isFirstInput = true;

    // 숫자를 다 지웠을 때 다시 띄워줄 안내 문구
    private string placeholderText = "방이 몇층에 위치해 있나요?";
    private string currentPlaceholderText;

    private void Start()
    {
        // 1. 숫자 버튼 0~9 자동 연결 (인스펙터 노가다 방지)
        for (int i = 0; i < numberButtons.Length; i++)
        {
            if (numberButtons[i] != null)
            {
                // 클로저(Closure) 문제를 방지하기 위해 지역 변수에 저장
                string numString = i.ToString();

                // 버튼이 눌렸을 때 실행할 함수를 코드로 직접 연결
                PressableButtonClickGuard.AddClick(numberButtons[i], () => OnClick_Number(numString));
            }
        }

        // 2. 기능 버튼 연결
        if (btn_Backspace != null)
            PressableButtonClickGuard.AddClick(btn_Backspace, OnClick_Backspace);

        if (btn_Done != null)
            PressableButtonClickGuard.AddClick(btn_Done, OnClick_Done);
    }

    // ==========================================
    // 외부(BuildingInfo 등)에서 넘버패드를 켤 때 호출하는 함수
    // ==========================================
    public void OpenNumpad(TMP_Text textComponentToUpdate)
    {
        OpenNumpad(textComponentToUpdate, null);
    }

    public void OpenNumpad(TMP_Text textComponentToUpdate, UnityAction onCloseRequested)
    {
        OpenNumpad(textComponentToUpdate, onCloseRequested, placeholderText);
    }

    public void OpenNumpad(TMP_Text textComponentToUpdate, UnityAction onCloseRequested, string placeholder)
    {
        targetText = textComponentToUpdate;
        closeRequested = onCloseRequested;
        currentPlaceholderText = string.IsNullOrEmpty(placeholder) ? placeholderText : placeholder;

        // 1. 기존 UI 텍스트 가져오기
        string existingText = targetText.text;

        // 2. 안내 문구이거나 비어있는 경우 첫 입력 상태로 세팅
        if (string.IsNullOrEmpty(existingText) || existingText == currentPlaceholderText)
        {
            isFirstInput = true;
            targetText.text = currentPlaceholderText; // 만약을 위해 안내 문구로 확실히 초기화
        }
        else
        {
            // 3. 기존에 입력된 층수가 있다면 "층" 글자 및 공백을 제거하고 첫 입력 상태 해제
            isFirstInput = false;
            targetText.text = existingText.Replace("층", "").Trim();
        }
    }

    // ==========================================
    // 내부 버튼 작동 로직
    // ==========================================

    private void OnClick_Number(string num)
    {
        if (targetText != null)
        {
            // 만약 첫 입력이라면? -> 안내 문구를 지우고 상태를 변경
            if (isFirstInput)
            {
                targetText.text = "";
                isFirstInput = false;
            }

            // 누른 숫자를 텍스트 뒤에 이어 붙임
            targetText.text += num;
        }
    }

    private void OnClick_Backspace()
    {
        if (targetText != null)
        {
            // 안내 문구가 떠있는 상태(첫 입력 대기 상태)일 때 지우기 버튼을 누르면 무시
            if (isFirstInput) return;

            // 텍스트가 남아있을 경우 글자 하나 지우기
            if (targetText.text.Length > 0)
            {
                targetText.text = targetText.text.Substring(0, targetText.text.Length - 1);
            }

            // 하나를 지웠는데 다 지워져서 빈칸이 되었다면, 다시 안내 문구를 띄워주기
            if (targetText.text.Length == 0)
            {
                targetText.text = currentPlaceholderText;
                isFirstInput = true; // 다시 첫 입력 상태로 되돌림
            }
        }
    }

    private void OnClick_Done()
    {
        // 텍스트 연결을 끊고 넘버패드를 끕니다.
        targetText = null;
        UnityAction onCloseRequested = closeRequested;
        closeRequested = null;
        onCloseRequested?.Invoke();
    }
}
