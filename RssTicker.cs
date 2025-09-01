using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.ServiceModel.Syndication;

namespace DesktopMascot
{
    public class RssItem
    {
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Link { get; set; } = "";
        public DateTime PublishDate { get; set; } = DateTime.MinValue;
    }

    public class RssTicker : IDisposable
    {
        private readonly Timer _rotateTimer;
        private readonly Timer _fetchTimer;
        private readonly Queue<RssItem> _queue = new Queue<RssItem>();
        private readonly LinkedList<RssItem> _history = new LinkedList<RssItem>();
        private readonly HashSet<string> _seenLinks = new HashSet<string>();
        private readonly HttpClient _httpClient;
        private bool _paused = false;
        private bool _disposed = false;

        public RssItem Current { get; private set; }
        public bool IsLoading { get; private set; } = true;

        public event Action Updated;
        public event Action<string> ErrorOccurred;

        public RssTicker(int perItemSeconds, int refreshMinutes)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DesktopMascot/1.1 RSS Reader");

            _rotateTimer = new Timer(perItemSeconds * 1000) { AutoReset = true };
            _rotateTimer.Elapsed += (s, e) => { if (!_paused) NextInternal(); };

            _fetchTimer = new Timer(refreshMinutes * 60_000) { AutoReset = true };
            _fetchTimer.Elapsed += async (s, e) => await RefreshAsync();
        }

        public void Start()
        {
            _rotateTimer.Start();
            _fetchTimer.Start();
            Task.Run(async () => await RefreshAsync());
            
            if (Current == null)
                NextInternal();
        }

        public void Stop()
        {
            _rotateTimer?.Stop();
            _fetchTimer?.Stop();
        }

        public void HoverPause(bool pause)
        {
            _paused = pause;
        }

        public void RestartRotateCountdown()
        {
            _rotateTimer.Stop();
            _rotateTimer.Start();
        }

        public void Next()
        {
            NextInternal();
            RestartRotateCountdown();
            Updated?.Invoke();
        }

        public void Previous()
        {
            if (_history.Count >= 2)
            {
                var previous = _history.Last.Previous?.Value;
                if (previous != null)
                {
                    Current = previous;
                    _history.AddLast(previous);
                    if (_history.Count > 50)
                        _history.RemoveFirst();
                    
                    RestartRotateCountdown();
                    Updated?.Invoke();
                }
            }
        }

        private void NextInternal()
        {
            if (_queue.Count == 0) return;

            if (Current != null)
            {
                _history.AddLast(Current);
                if (_history.Count > 50)
                    _history.RemoveFirst();
            }

            Current = _queue.Dequeue();
            Updated?.Invoke();
        }

        public async Task RefreshAsync()
        {
            try
            {
                IsLoading = true;
                var feeds = Settings.Current.Feeds ?? new[] { "https://www.gizmodo.jp/index.xml" };
                var allItems = new List<RssItem>();

                foreach (var feedUrl in feeds)
                {
                    try
                    {
                        var items = await FetchFeedAsync(feedUrl);
                        allItems.AddRange(items);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"フィード取得エラー ({feedUrl}): {ex.Message}");
                        ErrorOccurred?.Invoke($"フィード取得失敗: {feedUrl}");
                    }
                }

                var newItems = allItems
                    .Where(item => !_seenLinks.Contains(item.Link))
                    .OrderByDescending(item => item.PublishDate)
                    .ToList();

                foreach (var item in newItems)
                {
                    _queue.Enqueue(item);
                    _seenLinks.Add(item.Link);
                    
                    if (_seenLinks.Count > 1000)
                    {
                        _seenLinks.Clear();
                    }
                }

                if (Current == null && _queue.Count > 0)
                {
                    NextInternal();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RSS更新エラー: {ex.Message}");
                ErrorOccurred?.Invoke("RSS更新失敗");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<List<RssItem>> FetchFeedAsync(string feedUrl)
        {
            var response = await _httpClient.GetStringAsync(feedUrl);
            var xml = XDocument.Parse(response);
            var items = new List<RssItem>();

            var rssItems = xml.Descendants("item")
                .Concat(xml.Descendants("entry"));

            foreach (var element in rssItems)
            {
                var item = new RssItem();

                item.Title = GetElementValue(element, "title");
                item.Link = GetElementValue(element, "link");
                item.Summary = GetElementValue(element, "description") ??
                              GetElementValue(element, "summary") ??
                              GetElementValue(element, "content");

                var pubDate = GetElementValue(element, "pubDate") ??
                             GetElementValue(element, "published") ??
                             GetElementValue(element, "updated");

                if (DateTime.TryParse(pubDate, out var date))
                    item.PublishDate = date;

                if (!string.IsNullOrEmpty(item.Title) && !string.IsNullOrEmpty(item.Link))
                {
                    item.Summary = CleanSummary(item.Summary);
                    items.Add(item);
                }
            }

            return items;
        }

        private string GetElementValue(XElement parent, string elementName)
        {
            return parent.Descendants(elementName).FirstOrDefault()?.Value?.Trim();
        }

        private string CleanSummary(string summary)
        {
            if (string.IsNullOrEmpty(summary))
                return "";

            var cleaned = Regex.Replace(summary, @"<[^>]+>", "");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            var maxChars = Settings.Current.Rss.MaxSummaryChars;
            if (cleaned.Length > maxChars)
                cleaned = cleaned.Substring(0, maxChars) + "…";

            return cleaned;
        }

        public string GetDisplayText()
        {
            if (Current == null)
            {
                return IsLoading ? "読み込み中…" : "記事がありません";
            }

            // タイトルと説明文を常に表示
            if (!string.IsNullOrEmpty(Current.Summary))
            {
                return $"{Current.Title}\n\n{Current.Summary}";
            }

            return Current.Title;
        }

        public RssItem GetCurrentItem()
        {
            return Current;
        }

        public void NextItem()
        {
            NextInternal();
        }

        public void PreviousItem()
        {
            if (_history.Count == 0) return;

            var currentNode = _history.Find(Current);
            if (currentNode?.Previous != null)
            {
                Current = currentNode.Previous.Value;
                Updated?.Invoke();
            }
        }

        public void Pause()
        {
            _paused = true;
        }

        public void Resume()
        {
            _paused = false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _rotateTimer?.Dispose();
                _fetchTimer?.Dispose();
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}