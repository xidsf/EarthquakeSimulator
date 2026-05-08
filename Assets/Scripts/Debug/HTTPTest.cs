using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class HTTPTest : MonoBehaviour
{
    [Header("Server")]
    [SerializeField]
    private string detectUrl = "https://api.earquake.xyz/detect";

    [Header("Folder Upload")]
    [Tooltip("Application.persistentDataPath 아래의 상대 폴더명")]
    [SerializeField]
    private string imageFolderName = "FurnitureCaptureImages";

    [Header("Inspector Texture Upload")]
    [Tooltip("테스트용으로 직접 넣을 이미지들")]
    [SerializeField]
    private Texture2D[] testImages;

    [Header("Upload Option")]
    [SerializeField]
    private float delayBetweenUploads = 0.1f;

    [SerializeField]
    private int requestTimeoutSeconds = 20;

    private bool isUploading = false;

    // ------------------------------------------------------------
    // 1. persistentDataPath 하위 폴더의 이미지들을 전송
    // ------------------------------------------------------------
    public void UploadImagesFromPersistentFolder()
    {
        if (isUploading)
        {
            Debug.LogWarning("이미 업로드 중입니다.");
            return;
        }

        string folderPath = Path.Combine(Application.persistentDataPath, imageFolderName);
        StartCoroutine(UploadImageFolderCoroutine(folderPath));
    }

    // ------------------------------------------------------------
    // 2. 특정 절대 경로 폴더의 이미지들을 전송
    //    Unity Editor 테스트용으로 사용하기 좋음
    //    HoloLens2 빌드에서는 권한 문제를 피하려면 persistentDataPath 사용 권장
    // ------------------------------------------------------------
    public void UploadImagesFromFolderPath(string folderPath)
    {
        if (isUploading)
        {
            Debug.LogWarning("이미 업로드 중입니다.");
            return;
        }

        StartCoroutine(UploadImageFolderCoroutine(folderPath));
    }

    // ------------------------------------------------------------
    // 3. Inspector에 넣은 Texture2D 이미지들을 전송
    // ------------------------------------------------------------
    public void UploadInspectorTextures()
    {
        if (isUploading)
        {
            Debug.LogWarning("이미 업로드 중입니다.");
            return;
        }

        StartCoroutine(UploadTextureArrayCoroutine(testImages));
    }

    // ------------------------------------------------------------
    // 4. 런타임에서 얻은 Texture2D 하나 전송
    //    예: HoloLens 카메라 캡처 결과, RenderTexture 변환 결과 등
    // ------------------------------------------------------------
    public void UploadSingleTexture(Texture2D texture, string fileName = "capture.jpg")
    {
        if (texture == null)
        {
            Debug.LogError("전송할 Texture2D가 없습니다.");
            return;
        }

        StartCoroutine(UploadTextureCoroutine(texture, fileName, 0, 1));
    }

    // ------------------------------------------------------------
    // Folder Upload Coroutine
    // ------------------------------------------------------------
    private IEnumerator UploadImageFolderCoroutine(string folderPath)
    {
        isUploading = true;

        if (!Directory.Exists(folderPath))
        {
            Debug.LogError($"이미지 폴더가 존재하지 않습니다: {folderPath}");
            isUploading = false;
            yield break;
        }

        string[] imagePaths = GetImageFiles(folderPath);

        if (imagePaths.Length == 0)
        {
            Debug.LogWarning($"전송할 이미지가 없습니다: {folderPath}");
            isUploading = false;
            yield break;
        }

        Debug.Log($"이미지 업로드 시작: {imagePaths.Length}개");
        Debug.Log($"폴더 경로: {folderPath}");

        for (int i = 0; i < imagePaths.Length; i++)
        {
            string path = imagePaths[i];
            byte[] imageBytes;

            try
            {
                imageBytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"이미지 읽기 실패: {path}\n{e.Message}");
                continue;
            }

            string fileName = Path.GetFileName(path);

            yield return UploadImageBytesCoroutine(
                imageBytes,
                fileName,
                i,
                imagePaths.Length
            );

            if (delayBetweenUploads > 0f)
                yield return new WaitForSeconds(delayBetweenUploads);
        }

        Debug.Log("이미지 업로드 완료");
        isUploading = false;
    }

    // ------------------------------------------------------------
    // Texture2D Array Upload Coroutine
    // ------------------------------------------------------------
    private IEnumerator UploadTextureArrayCoroutine(Texture2D[] textures)
    {
        isUploading = true;

        if (textures == null || textures.Length == 0)
        {
            Debug.LogWarning("전송할 Texture2D가 없습니다.");
            isUploading = false;
            yield break;
        }

        Debug.Log($"Texture2D 업로드 시작: {textures.Length}개");

        for (int i = 0; i < textures.Length; i++)
        {
            Texture2D texture = textures[i];

            if (texture == null)
            {
                Debug.LogWarning($"Texture2D가 비어있습니다. index: {i}");
                continue;
            }

            string fileName = $"capture_{i:D3}.jpg";

            yield return UploadTextureCoroutine(
                texture,
                fileName,
                i,
                textures.Length
            );

            if (delayBetweenUploads > 0f)
                yield return new WaitForSeconds(delayBetweenUploads);
        }

        Debug.Log("Texture2D 업로드 완료");
        isUploading = false;
    }

    // ------------------------------------------------------------
    // Texture2D 하나를 JPG로 변환 후 전송
    // ------------------------------------------------------------
    private IEnumerator UploadTextureCoroutine(
        Texture2D texture,
        string fileName,
        int index,
        int totalCount
    )
    {
        byte[] jpgBytes;

        try
        {
            jpgBytes = texture.EncodeToJPG(90);
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"Texture2D JPG 변환 실패: {fileName}\n" +
                $"Texture Import Settings에서 Read/Write Enabled가 꺼져있을 수 있습니다.\n" +
                e.Message
            );
            yield break;
        }

        yield return UploadImageBytesCoroutine(
            jpgBytes,
            fileName,
            index,
            totalCount
        );
    }

    // ------------------------------------------------------------
    // 실제 서버 전송부
    // Flask 서버의 request.files["image"]와 맞추기 위해 field name은 반드시 "image"
    // ------------------------------------------------------------
    private IEnumerator UploadImageBytesCoroutine(
        byte[] imageBytes,
        string fileName,
        int index,
        int totalCount
    )
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            Debug.LogError($"이미지 바이트가 비어있습니다: {fileName}");
            yield break;
        }

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();

        // 서버의 request.files["image"]와 일치해야 함
        formData.Add(new MultipartFormFileSection(
            "image",
            imageBytes,
            fileName,
            GetMimeType(fileName)
        ));

        // 현재 Flask 서버는 이 값들을 사용하지 않지만,
        // 이후 SAM3 인식 결과와 실제 공간 배치를 연결할 때 유용함
        formData.Add(new MultipartFormDataSection("client_frame_index", index.ToString()));
        formData.Add(new MultipartFormDataSection("client_total_count", totalCount.ToString()));
        formData.Add(new MultipartFormDataSection("source", "hololens2_furniture_capture"));

        using (UnityWebRequest request = UnityWebRequest.Post(detectUrl, formData))
        {
            request.timeout = requestTimeoutSeconds;

            Debug.Log($"[{index + 1}/{totalCount}] 이미지 전송 시작: {fileName}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[{index + 1}/{totalCount}] 이미지 전송 실패\n" +
                    $"file: {fileName}\n" +
                    $"responseCode: {request.responseCode}\n" +
                    $"error: {request.error}\n" +
                    $"body: {request.downloadHandler.text}"
                );

                yield break;
            }

            string responseText = request.downloadHandler.text;

            Debug.Log(
                $"[{index + 1}/{totalCount}] 이미지 전송 성공\n" +
                $"file: {fileName}\n" +
                $"response: {responseText}"
            );

            DetectResponse response = null;

            try
            {
                response = JsonUtility.FromJson<DetectResponse>(responseText);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"응답 JSON 파싱 실패: {e.Message}");
            }

            if (response != null && response.objects != null)
            {
                foreach (DetectedFurnitureObject obj in response.objects)
                {
                    Debug.Log(
                        $"Detected Object - " +
                        $"id: {obj.id}, " +
                        $"label: {obj.label}, " +
                        $"confidence: {obj.confidence}"
                    );
                }
            }
        }
    }

    // ------------------------------------------------------------
    // 이미지 확장자 필터
    // ------------------------------------------------------------
    private string[] GetImageFiles(string folderPath)
    {
        List<string> result = new List<string>();

        string[] extensions =
        {
            "*.jpg",
            "*.jpeg",
            "*.png"
        };

        foreach (string extension in extensions)
        {
            result.AddRange(Directory.GetFiles(folderPath, extension, SearchOption.TopDirectoryOnly));
        }

        result.Sort();
        return result.ToArray();
    }

    private string GetMimeType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        switch (ext)
        {
            case ".png":
                return "image/png";

            case ".jpg":
            case ".jpeg":
            default:
                return "image/jpeg";
        }
    }
}

// ------------------------------------------------------------
// Flask 응답 JSON 구조
// ------------------------------------------------------------
[Serializable]
public class DetectResponse
{
    public string frame_id;
    public DetectedFurnitureObject[] objects;
}

[Serializable]
public class DetectedFurnitureObject
{
    public string id;
    public string label;
    public float confidence;
    public int[] bbox;
    public string mesh_url;
}