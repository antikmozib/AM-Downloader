// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace AMDownloader.Helpers
{
    public static class AppUpdateService
    {
        public static async Task<string> GetUpdateUrl(string server,
            string appName,
            string appVersion,
            HttpClient httpClient = null)
        {
            httpClient ??= new HttpClient();

            appName = appName.Replace(" ", ""); // replace spaces in url

            try
            {
                using var response = await httpClient.GetAsync(server + "?appname=" + appName + "&version=" + appVersion);

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
