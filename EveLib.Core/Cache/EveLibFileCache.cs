﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using eZet.EveLib.Core.Util;

namespace eZet.EveLib.Core.Cache {
    public class EveLibFileCache : IEveLibCache {
        private static readonly SHA1CryptoServiceProvider Sha1 = new SHA1CryptoServiceProvider();

        private readonly IDictionary<string, DateTime> _register = new Dictionary<string, DateTime>();

        private readonly TraceSource _trace = new TraceSource("EveLib", SourceLevels.All);

        private bool _isInitialized;

        public async Task StoreAsync(Uri uri, DateTime cacheTime, string data) {
            _trace.TraceEvent(TraceEventType.Start, 0, "EveLibFileCache.StoreAsync()");
            _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:Uri: {0}", uri);
            _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:Cache Expiry: {0}", cacheTime);

            string key = getHash(uri);
            _register[key] = cacheTime;
            if (!Directory.Exists(Config.CachePath)) {
                _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:Creating cache directory");
                Directory.CreateDirectory(Config.CachePath);
            }
            try {
                Task cacheTask = writeCacheDataToDiskAsync(uri, data);
                Task registerTask = writeRegisterToDiskAsync();
                await Task.WhenAll(cacheTask, registerTask).ConfigureAwait(false);
            }
            catch (Exception) {
                _trace.TraceEvent(TraceEventType.Error, 0, "EveLibFileCache:An error occured while writing to cache");
            }
            _trace.TraceEvent(TraceEventType.Stop, 0, "EveLibFileCache.StoreAsync()");
        }

        public async Task<string> LoadAsync(Uri uri) {
            _trace.TraceEvent(TraceEventType.Start, 0, "EveLibFileCache.LoadAsync()");
            await initAsync().ConfigureAwait(false);
            string data = null;
            _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:CacheRegisterLookupUri: {0}", uri);
            string hash = getHash(uri);
            _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:CacheRegisterLookupHash: {0}", hash);
            DateTime cacheExpirationTime;
            bool found = _register.TryGetValue(hash, out cacheExpirationTime);
            _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:CacheRegisterHit: {0}", found);
            if (found) {
                bool validCache = DateTime.UtcNow < cacheExpirationTime;
                _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:CacheIsValid: {0} ({1})", validCache,
                    cacheExpirationTime);
                if (validCache) {
                    string filePath = Config.CachePath + Config.Separator + hash;
                    bool fileExist = File.Exists(filePath);
                    _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:CacheDataFound: {0}", fileExist);
                    if (File.Exists(filePath)) {
                        try {
                            data =
                                await
                                    FileAsync.ReadAllTextAsync(Config.CachePath + Config.Separator + getHash(uri))
                                        .ConfigureAwait(false);
                            _trace.TraceEvent(TraceEventType.Verbose, 0,
                                "EveLibFileCache:Data successfully loaded from cache: {0}",
                                filePath);
                        }
                        catch (Exception) {
                            _trace.TraceEvent(TraceEventType.Error, 0,
                                "EveLibFileCache:Cache data could not be loaded: {0}", filePath);
                        }
                    }
                }
            }
            _trace.TraceEvent(TraceEventType.Stop, 0, "EveLibFileCache.LoadAsync()");
            return data;
        }

        public virtual bool TryGetExpirationDate(Uri uri, out DateTime value) {
            string key = getHash(uri);
            return _register.TryGetValue(key, out value);
        }

        private async Task initAsync() {
            if (_isInitialized) return;
            await loadRegisterFromDiskAsync().ConfigureAwait(false);
            _isInitialized = true;
        }

        private Task writeRegisterToDiskAsync() {
            _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:Writing cache register to disk");
            return FileAsync.WriteAllLinesAsync(Config.CacheRegister,
                _register.Select(x => x.Key + "," + x.Value.ToString(CultureInfo.InvariantCulture)));
        }

        private Task writeCacheDataToDiskAsync(Uri uri, string data) {
            _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:Writing cache data to disk: {0}", uri);
            return FileAsync.WriteAllTextAsync(Config.CachePath + Path.DirectorySeparatorChar + getHash(uri), data);
        }

        private static string getHash(Uri uri) {
            string fileName = uri.PathAndQuery;
            byte[] hash = Sha1.ComputeHash(Encoding.Unicode.GetBytes(fileName));
            return BitConverter.ToString(hash).Replace("-", "");
        }

        private async Task loadRegisterFromDiskAsync() {
            _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:Loading cache register: {0}",
                Config.CacheRegister);
            if (!Directory.Exists(Config.CachePath)) {
                _trace.TraceEvent(TraceEventType.Warning, 0, "EveLibFileCache:Cache directory not found: {0}",
                    Config.CachePath);
                return;
            }
            if (!File.Exists(Config.CacheRegister)) {
                _trace.TraceEvent(TraceEventType.Warning, 0, "EveLibFileCache:Cache register not found: {0}",
                    Config.CacheRegister);
                return;
            }
            try {
                // read all lines
                string[] data = await
                    FileAsync.ReadAllLinesAsync(Config.CacheRegister).ConfigureAwait(false);
                foreach (string entry in data) {
                    string[] split = entry.Split(',');
                    DateTime cacheValidUntil = DateTime.Parse(split[1], CultureInfo.InvariantCulture);
                    string fileName = split[0];
                    // if cache is still valid we insert it
                    if (cacheValidUntil > DateTime.UtcNow)
                        _register[fileName] = cacheValidUntil;
                    else {
                        // if cache is out of date we delete the data
                        if (File.Exists(Config.CachePath + Config.Separator + fileName)) {
                            File.Delete(Config.CachePath + Config.Separator + fileName);
                        }
                    }
                }
                _trace.TraceEvent(TraceEventType.Verbose, 0, "EveLibFileCache:CacheRegisterLoaded");
            }
            catch (Exception) {
                _trace.TraceEvent(TraceEventType.Error, 0, "EveLibFileCache:Could not load cache register");
            }
        }
    }
}