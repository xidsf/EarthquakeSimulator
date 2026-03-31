using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIRaycastDebugger : MonoBehaviour
{
    void Update()
    {
        // 마우스 왼쪽 버튼(0)을 누르는 순간 실행
        if (Input.GetMouseButtonDown(0))
        {
            CheckUIObjectsUnderMouse();
        }
    }

    void CheckUIObjectsUnderMouse()
    {
        // 1. 현재 씬에 EventSystem이 없으면 작동하지 않으므로 예외 처리
        if (EventSystem.current == null)
        {
            Debug.LogWarning("씬에 EventSystem이 없습니다! UI 레이캐스트를 쏠 수 없습니다.");
            return;
        }

        // 2. 마우스 포인터의 위치 데이터를 담을 객체 생성
        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = Input.mousePosition;

        // 3. 레이캐스트에 맞은 UI들을 담을 리스트 생성
        List<RaycastResult> results = new List<RaycastResult>();

        // 4. 마우스 위치에서 화면 안쪽으로 UI 레이캐스트 발사!
        EventSystem.current.RaycastAll(pointerData, results);

        // 5. 결과 출력
        if (results.Count > 0)
        {
            Debug.Log($"<color=cyan>--- 마우스 클릭 위치에서 잡힌 UI 총 {results.Count}개 ---</color>");
            for (int i = 0; i < results.Count; i++)
            {
                // results[0]이 화면 맨 앞(유저와 가장 가까운)에 있는 UI입니다.
                Debug.Log($"[{i}] 감지된 오브젝트 이름: <b>{results[i].gameObject.name}</b>");
            }
        }
        else
        {
            Debug.Log("<color=red>이 위치에는 클릭을 받을 수 있는 UI(Raycast Target)가 없습니다!</color>");
        }
    }
}