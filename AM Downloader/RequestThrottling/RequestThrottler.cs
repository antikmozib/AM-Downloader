// Copyright (C) 2020-2023 Antik Mozib. All rights reserved.

using AMDownloader.RequestThrottling.Model;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace AMDownloader.RequestThrottling
{
    internal class RequestThrottler
    {
        private readonly int _interval;
        private readonly ConcurrentQueue<RequestModel> _requestList;
        private CancellationTokenSource _cts;
        private CancellationToken _ct;

        public RequestThrottler(int interval)
        {
            _requestList = new ConcurrentQueue<RequestModel>();
            _interval = interval;
        }

        public RequestModel? Has(string value)
        {
            if (_requestList.IsEmpty) return null;
            var items = (from item in _requestList where item.Url == value select item).ToArray();
            if (items.Count() > 0)
            {
                return items[0];
            }
            else
            {
                return null;
            }
        }

        public void Keep(string value, long? totalBytesToDownload = null, HttpStatusCode? status = null)
        {
            RequestModel requestModel;
            requestModel.Url = value;
            requestModel.SeenAt = DateTime.Now;
            requestModel.TotalBytesToDownload = totalBytesToDownload;
            requestModel.StatusCode = status;
            _requestList.Enqueue(requestModel);
            if (_cts == null)
            {
                Task.Run(async () => await RemoveUrls());
            }
        }

        private async Task RemoveUrls()
        {
            _cts = new CancellationTokenSource();
            _ct = _cts.Token;

            while (!_requestList.IsEmpty)
            {
                if (_ct.IsCancellationRequested)
                {
                    break;
                }
                if (!_requestList.TryDequeue(out RequestModel requestModel))
                {
                    continue;
                }
                TimeSpan diff = DateTime.Now.Subtract(requestModel.SeenAt);
                if (diff.TotalSeconds < _interval)
                {
                    int delay = _interval - (int)diff.TotalMilliseconds;
                    if (delay > 0) await Task.Delay(delay);
                }
            }
            _cts = null;
            _ct = default;
        }
    }
}