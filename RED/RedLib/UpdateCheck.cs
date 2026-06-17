using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace RED
{
    internal static class UpdateCheck
    {
        private const string ReleasesUrl = "https://api.github.com/repos/SysAdminDoc/REDplusplus/releases/latest";
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        private static readonly Regex TagPattern = new Regex(@"v?(\d+\.\d+\.\d+)", RegexOptions.Compiled);

        internal class Result
        {
            public bool NewerAvailable { get; set; }
            public string LatestVersion { get; set; }
            public string ReleaseUrl { get; set; }
        }

        internal static Result Check(string currentVersion, string stateFilePath)
        {
            if (string.IsNullOrWhiteSpace(currentVersion))
                return null;

            string lastETag = null;
            DateTime lastCheck = DateTime.MinValue;

            if (File.Exists(stateFilePath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(stateFilePath);
                    if (lines.Length >= 1) DateTime.TryParse(lines[0], out lastCheck);
                    if (lines.Length >= 2) lastETag = lines[1];
                }
                catch { }
            }

            if ((DateTime.UtcNow - lastCheck) < CheckInterval)
                return null;

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RED++", currentVersion));
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                    if (!string.IsNullOrEmpty(lastETag))
                        client.DefaultRequestHeaders.IfNoneMatch.Add(new EntityTagHeaderValue(lastETag));

                    var task = client.GetAsync(ReleasesUrl);
                    task.Wait();
                    var response = task.Result;

                    if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                    {
                        SaveState(stateFilePath, DateTime.UtcNow, lastETag);
                        return null;
                    }

                    response.EnsureSuccessStatusCode();
                    string newETag = response.Headers.ETag?.Tag;

                    var readTask = response.Content.ReadAsStringAsync();
                    readTask.Wait();
                    string responseBody = readTask.Result;

                    SaveState(stateFilePath, DateTime.UtcNow, newETag);

                    string tagName = ExtractJsonValue(responseBody, "tag_name");
                    string htmlUrl = ExtractJsonValue(responseBody, "html_url");
                    if (string.IsNullOrEmpty(tagName))
                        return null;

                    var tagMatch = TagPattern.Match(tagName);
                    if (!tagMatch.Success)
                        return null;

                    string latestVersion = tagMatch.Groups[1].Value;
                    bool newer = CompareVersions(latestVersion, currentVersion) > 0;

                    return new Result
                    {
                        NewerAvailable = newer,
                        LatestVersion = latestVersion,
                        ReleaseUrl = htmlUrl ?? ("https://github.com/SysAdminDoc/REDplusplus/releases/tag/" + tagName)
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        private static void SaveState(string path, DateTime checkTime, string etag)
        {
            try
            {
                File.WriteAllLines(path, new[]
                {
                    checkTime.ToString("o"),
                    etag ?? string.Empty
                });
            }
            catch { }
        }

        private static string ExtractJsonValue(string json, string key)
        {
            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]+)\"";
            var m = Regex.Match(json, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }

        internal static int CompareVersions(string a, string b)
        {
            var partsA = a.Split('.');
            var partsB = b.Split('.');
            int len = Math.Max(partsA.Length, partsB.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < partsA.Length ? ParseInt(partsA[i]) : 0;
                int vb = i < partsB.Length ? ParseInt(partsB[i]) : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, out v) ? v : 0;
        }
    }
}
