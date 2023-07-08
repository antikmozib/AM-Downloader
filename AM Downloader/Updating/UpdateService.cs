// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;

namespace AMDownloader.Updating
{
    public struct UpdateInfo
    {
        [JsonPropertyName("app")]
        public string AppName { get; set; }

        [JsonPropertyName("arch")]
        public string Arch { get; set; }

        [JsonPropertyName("version")]
        public VersionInfo Versions { get; set; }

        [JsonPropertyName("released")]
        public string Released { get; set; }

        [JsonPropertyName("file_url")]
        public string FileUrl { get; set; }

        [JsonPropertyName("update_info_url")]
        public string UpdateInfoUrl { get; set; }
    }

    public struct VersionInfo
    {
        [JsonPropertyName("major")]
        public int Major { get; set; }
        [JsonPropertyName("minor")]
        public int Minor { get; set; }
        [JsonPropertyName("build")]
        public int Build { get; set; }
        [JsonPropertyName("revision")]
        public int Revision { get; set; }
        public override string ToString()
        {
            return $"{Major}.{Minor}.{Build}.{Revision}";
        }
    }

    public static class UpdateService
    {
        private const string ApiAddress = @"https://mozib.io/downloads/update.php";

        public static async Task<UpdateInfo> GetLatestUpdateInfoAsync(string appName, HttpClient httpClient = null)
        {
            httpClient ??= new HttpClient();
            appName = HttpUtility.UrlEncode(appName);

            try
            {
                using var response = await httpClient.GetAsync(ApiAddress + "?appname=" + appName);

                if (response.IsSuccessStatusCode)
                {
                    return await JsonSerializer.DeserializeAsync<UpdateInfo>(await response.Content.ReadAsStreamAsync());
                }
                else
                {
                    throw new HttpRequestException($"The API returned an invalid status code. ({response.StatusCode})");
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
        }

        public static bool IsUpdateAvailable(UpdateInfo latestUpdateInfo, string currentVer)
        {
            Version newVer = new(latestUpdateInfo.Versions.ToString());
            Version oldVer = new(currentVer);
            return newVer.CompareTo(oldVer) > 0;
        }
    }
}
