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

        [JsonPropertyName("fileUrl")]
        public string FileUrl { get; set; }

        [JsonPropertyName("updateInfoUrl")]
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
        public override readonly string ToString()
        {
            return $"{Major}.{Minor}.{Build}.{Revision}";
        }
    }

    public static class UpdateService
    {
        private const string ApiAddress = @"https://mozib.io/downloads/update";

        /// <summary>
        /// Requests the update API to send information about the latest available update for <paramref name="appName"/>.
        /// </summary>
        /// <param name="appName">The name of the app for which to get the latest update information.</param>
        /// <param name="httpClient">The <see cref="HttpClient"/> through which to establish communication. A new one is created
        /// if none is supplied.</param>
        /// <returns>A <see cref="Task"/> representing an <see cref="UpdateInfo"/> which contains information about the latest 
        /// available update to <paramref name="appName"/>.</returns>
        /// <exception cref="Exception"></exception>
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

        /// <summary>
        /// Compares two versions to determine if an update is available.
        /// </summary>
        /// <param name="latestVer">The latest available version.</param>
        /// <param name="currentVer">The version which is currently installed on the system.</param>
        /// <returns><see langword="true"/> if an update is available.</returns>
        public static bool IsUpdateAvailable(VersionInfo latestVer, string currentVer)
        {
            Version newVer = new(latestVer.ToString());
            Version oldVer = new(currentVer);
            return newVer.CompareTo(oldVer) > 0;
        }
    }
}
