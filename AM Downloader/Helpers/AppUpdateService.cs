// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMDownloader.Helpers
{
    public static class AppUpdateService
    {
        public const string ApiAddress = @"https://mozib.io/downloads/update.php";

        public static async Task<string> GetUpdateUrl(string appName, string appVersion, HttpClient httpClient = null)
        {
            if (appName.Contains(' ') || appVersion.Contains(' '))
            {
                throw new ArgumentException(
                    $"Parameters {nameof(appName)} and {nameof(appVersion)} must not contain spaces.");
            }

            httpClient ??= new HttpClient();

            try
            {
                using var response = await httpClient.GetAsync(ApiAddress + "?appname=" + appName + "&version=" + appVersion);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
