using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LazyImage : RawImage
{
    private static readonly Dictionary<string, Action<Texture2D>> FetchRequests =
        new Dictionary<string, Action<Texture2D>>();

    public string Url
    {
        get => _url;
        set => _url = value;
    }

    [SerializeField] private string _url;
    [SerializeField] private bool _loadOnAwake;

    private bool _loaded;

    protected override void Awake()
    {
        base.Awake();
        if (_loadOnAwake)
        {
            Load();
        }
    }

    public void Load()
    {
        _loaded = false;
        var self = this;
        FetchAndCacheTexture(Url, texture2D =>
        {
            if (self == null) return;

            texture = texture2D;
            var ratio = texture2D.width / (float) texture2D.height;
            Canvas.ForceUpdateCanvases();
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rectTransform.rect.width / ratio);
            _loaded = true;
        });
    }

    public void LoadUrl(string url)
    {
        Url = url;
        Load();
    }

    public bool IsLoadCompleted()
    {
        return _loaded;
    }

    public static void FetchAndCacheTexture(string url, Action<Texture2D> callback = null)
    {
        if (string.IsNullOrEmpty(url.Trim())) return;

        if (FetchRequests.ContainsKey(url))
        {
            FetchRequests[url] += callback;
            return;
        }

        var hash = LazyImplementationHelper.CreateHash(url);
        GetCachedTexture(hash /*).ContinueWith(*/, cachedTexture =>
        {
            if (cachedTexture != null)
            {
                callback?.Invoke(cachedTexture);
                return;
            }

            FetchRequests.Add(url, callback);

            var op = UnityWebRequestTexture.GetTexture(url, false);
            var wr = op.SendWebRequest();
            wr.completed += operation =>
            {
                if (op.isHttpError || op.isNetworkError)
                {
                    Debug.LogError($"Error Getting Image {url} ERROR:{op.error}");

                    FetchRequests.Remove(url);
                    return;
                }

                var tex = ((DownloadHandlerTexture) op.downloadHandler).texture;
                FetchRequests[url]?.Invoke(tex);
                FetchRequests.Remove(url);
                CacheTexture(hash, tex);
            };
        });
    }

    private static void CacheTexture(string hash, Texture2D tex)
    {
        File.WriteAllBytes(Path.Combine(Application.temporaryCachePath, hash), tex.EncodeToPNG());
    }

    private static void GetCachedTexture(string hash, Action<Texture2D> callback)
    {
        var path = Path.Combine(Application.temporaryCachePath, hash);
        if (!File.Exists(path))
        {
            callback?.Invoke(null);
            return;
        }
        
        var op = UnityWebRequestTexture.GetTexture(path, false);
        op.SendWebRequest().completed += operation =>
        {
            if (op.isHttpError || op.isNetworkError)
            {
                Debug.LogError($"Error Getting Image {path} ERROR:{op.error}");
                return;
            }

            callback?.Invoke(((DownloadHandlerTexture) op.downloadHandler).texture);
        };
    }
}