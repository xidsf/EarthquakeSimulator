using System.Collections;
using UnityEngine;
using TMPro; // TextMeshPro 사용 시

public class LoadingPlateManager : MonoBehaviour
{
    public static LoadingPlateManager Instance;

    [Header("UI References")]
    public GameObject loadingPanel; // 로딩창 전체 패널
    public TextMeshProUGUI loadingText; // 글자가 표시될 텍스트 컴포넌트

    [Header("Settings")]
    public float dotAnimationSpeed = 0.5f; // 점이 찍히는 속도 (초 단위)

    private Coroutine currentAnimation;
    private string currentBaseText;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        loadingPanel.SetActive(false);
    }

    // 로딩창 켜기
    public void ShowLoading(string baseText)
    {
        currentBaseText = baseText;
        loadingPanel.SetActive(true);

        if (currentAnimation != null) StopCoroutine(currentAnimation);
        currentAnimation = StartCoroutine(AnimateDots());
    }

    // 로딩창 끄기
    public void HideLoading()
    {
        loadingPanel.SetActive(false);
        if (currentAnimation != null)
        {
            StopCoroutine(currentAnimation);
            currentAnimation = null;
        }
    }

    // 텍스트 내용만 업데이트 (결과 창 로딩중 등 연속 전환 시 사용)
    public void UpdateText(string newBaseText)
    {
        currentBaseText = newBaseText;
    }

    // . -> .. -> ... 애니메이션 코루틴
    private IEnumerator AnimateDots()
    {
        int dotCount = 0;
        while (true)
        {
            // 점 개수에 따라 문자열 생성
            string dots = new string('.', dotCount);
            loadingText.text = currentBaseText + dots;

            dotCount++;
            if (dotCount > 3) dotCount = 0; // 0, 1, 2, 3 반복

            yield return new WaitForSeconds(dotAnimationSpeed);
        }
    }
}