﻿/*
MIT License

Copyright (c) 2019 Digital Ruby, LLC - https://www.digitalruby.com

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalRuby.IPBanCore
{
    /// <summary>
    /// Create a block firewall rule from ip addresses from a uri
    /// </summary>
    public class IPBanUriFirewallRule : IUpdater
    {
        private readonly IIPBanFirewall firewall;
        private readonly string rulePrefix;
        private readonly Uri uri;
        private readonly TimeSpan interval;
        private readonly HttpClient httpClient;

        private DateTime lastRun;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="firewall">The firewall to block with</param>
        /// <param name="rulePrefix">Firewall rule prefix</param>
        /// <param name="uri">Uri, can be either file or http(s).</param>
        /// <param name="interval">Interval to check uri for changes</param>
        public IPBanUriFirewallRule(IIPBanFirewall firewall, string rulePrefix, Uri uri, TimeSpan interval)
        {
            this.firewall = firewall;
            this.rulePrefix = rulePrefix;
            this.uri = uri;
            this.interval = interval;

            if (!uri.IsFile)
            {
                // ensure uri ends with slash
                if (!uri.ToString().EndsWith("/"))
                {
                    uri = new Uri(uri.ToString() + "/");
                }
                httpClient = new HttpClient { BaseAddress = uri };
            }
        }

        /// <summary>
        /// Cleanup all resources
        /// </summary>
        public void Dispose()
        {
            httpClient?.Dispose();
        }

        /// <summary>
        /// Update the updater
        /// </summary>
        /// <param name="cancelToken">Cancel token</param>
        /// <returns>Task</returns>
        public async Task Update(CancellationToken cancelToken)
        {
            DateTime now = IPBanService.UtcNow;
            if ((now - lastRun) >= interval)
            {
                lastRun = now;
                if (uri.IsFile)
                {
                    await ProcessResult(await File.ReadAllTextAsync(uri.LocalPath, cancelToken), cancelToken);
                }
                else
                {
                    HttpResponseMessage response = await httpClient.GetAsync(string.Empty, cancelToken);
                    response.EnsureSuccessStatusCode();
                    string text = await response.Content.ReadAsStringAsync();
                    await ProcessResult(text, cancelToken);
                }
            }
        }

        private Task ProcessResult(string text, CancellationToken cancelToken)
        {
            using StringReader reader = new StringReader(text);
            string line;
            List<IPAddressRange> ranges = new List<IPAddressRange>();
            int lines = 0;

            while ((line = reader.ReadLine()) != null)
            {
                if (lines++ > 10000)
                {
                    // prevent too many lines from crashing us
                    break;
                }

                // trim line, ignore if necessary
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("'") || line.StartsWith("REM") ||
                    !IPAddressRange.TryParse(line, out IPAddressRange range))
                {
                    continue;
                }
                ranges.Add(range);
            }

            return firewall.BlockIPAddresses(rulePrefix, ranges, null, cancelToken);
        }
    }
}