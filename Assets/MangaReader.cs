using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MangaReader : MonoBehaviour
{
    [SerializeField] private LazyImage _imagePrefab;
    [SerializeField] private RectTransform _list;

    [SerializeField] private InputField _titleId;
    [SerializeField] private Dropdown _chapter;

    private const string BASE_URL = "https://mangadex.cc/";
    private const string BASE_DATA_URL = "https://s5.mangadex.org/data";
    private const string API_MANGA = BASE_URL + "api/manga/";
    private const string API_CHAPTER = BASE_URL + "api/chapter/";

    private MangaResponse _currentManga;
    private readonly Dictionary<string, string> _chapters = new Dictionary<string, string>();

    private UnityWebRequest _mangaRequest;
    private Coroutine _chapterRoutine;

    public void LoadFromTitleId()
    {
        if (_chapterRoutine != null)
            StopCoroutine(_chapterRoutine);

        _mangaRequest?.Abort();
        var url = _titleId.text.Trim();
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Host.Contains("mangadex") || uri.Segments.Length < 3) return;

        var title = uri.Segments[2].Trim('/');

        if (string.IsNullOrEmpty(title)) return;

        _currentManga = null;
        _chapters.Clear();
        _chapter.ClearOptions();

        foreach (Transform child in _list)
        {
            Destroy(child.gameObject);
        }

        var titleId = long.Parse(title);
        _mangaRequest = UnityWebRequest.Get(API_MANGA + titleId);
        _mangaRequest.SendWebRequest().completed += _ =>
        {
            if (_mangaRequest.isHttpError || _mangaRequest.isNetworkError)
            {
                Debug.LogError($"Error while trying to get Manga info ERROR: {_mangaRequest.error}");
                return;
            }

            var options = new List<string>();
            var response = MangaResponse.FromJson(_mangaRequest.downloadHandler.text);
            foreach (var chapter in response.Chapters)
            {
                if (chapter.Value.LangCode == "gb")
                {
                    var chapterName = $"Vol. {chapter.Value.Volume} Ch. {chapter.Value.ChapterNo}";
                    if (string.IsNullOrEmpty(chapter.Value.ChapterNo))
                    {
                        chapterName = "Oneshot";
                    }
                    else if (!string.IsNullOrEmpty(chapter.Value.Title))
                    {
                        chapterName += $" - {chapter.Value.Title}";
                    }

                    _chapters[chapterName] = chapter.Key;
                    options.Add(chapterName);
                }
            }

            _chapter.AddOptions(options.Select(x =>
                {
                    var split = x.Split(' ');
                    return new {str = x, split = split.Length >= 4 ? split : new string[4]};
                })
                .OrderBy(x => float.Parse("0" + x.split[3]))
                .Select(x => x.str)
                .ToList());

            _currentManga = response;

            SetChapterNo(PlayerPrefs.GetInt(_currentManga.Manga.Title, 0));
            
            // Load the chapters
            _chapterRoutine = StartCoroutine(LoadMangaChapter());
        };
    }

    public void LoadNextChapter()
    {
        if (_currentManga == null)
        {
            Debug.LogError("No manga loaded");
            return;
        }

        SetChapterNo(GetChapterNo() + 1);
        StartCoroutine(LoadMangaChapter());
    }

    public void LoadPrevChapter()
    {
        if (_currentManga == null)
        {
            Debug.LogError("No manga loaded");
            return;
        }

        SetChapterNo(GetChapterNo() - 1);
        StartCoroutine(LoadMangaChapter());
    }

    private void SetChapterNo(int chapterNo)
    {
        var c = Mathf.Clamp(chapterNo, 0, _chapter.options.Count);
        PlayerPrefs.SetInt(_currentManga.Manga.Title, _chapter.value = c);
        _chapter.RefreshShownValue();
    }

    private string GetChapterName()
    {
        return _chapter.options[_chapter.value].text;
    }

    private int GetChapterNo()
    {
        return _chapter.value;
    }

    public void LoadChapter()
    {
        if (_chapterRoutine != null)
            StopCoroutine(_chapterRoutine);

        _chapterRoutine = StartCoroutine(LoadMangaChapter());
    }

    private IEnumerator LoadMangaChapter()
    {
        if (_currentManga == null)
        {
            Debug.LogError("No Manga loaded, trying to load");
            LoadFromTitleId();
            yield break;
        }

        var chapterName = GetChapterName();

        if (!_chapters.TryGetValue(chapterName, out var chapterId))
        {
            Debug.LogError($"Chapter {chapterName} not found!");
            yield break;
        }

        foreach (Transform child in _list)
        {
            Destroy(child.gameObject);
        }

        var chapterUrl = API_CHAPTER + chapterId;
        var req = UnityWebRequest.Get(chapterUrl);
        yield return req.SendWebRequest();

        // we got the shit

        var chapterResponse = ChapterResponse.FromJson(req.downloadHandler.text);
        foreach (var chapterFile in chapterResponse.PageArray)
        {
            if (chapterName != GetChapterName()) yield break;
            
            var imageLink = Path.Combine(chapterResponse.Server.Host.Contains("s4") ? BASE_DATA_URL : chapterResponse.Server.AbsoluteUri, chapterResponse.Hash, chapterFile);
            var lazyImage = Instantiate(_imagePrefab.gameObject, _list).GetComponent<LazyImage>();
            lazyImage.LoadUrl(imageLink);
            yield return new WaitUntil(lazyImage.IsLoadCompleted);
        }
    }
}