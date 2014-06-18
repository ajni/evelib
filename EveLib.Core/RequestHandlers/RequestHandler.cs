﻿using System;
using System.Net;
using System.Threading.Tasks;
using eZet.EveLib.Core.Exceptions;
using eZet.EveLib.Core.Serializers;
using eZet.EveLib.Core.Util;

namespace eZet.EveLib.Core.RequestHandlers {
    public class RequestHandler : IRequestHandler {
        public RequestHandler(IHttpRequester httpRequester, ISerializer serializer) {
            HttpRequester = httpRequester;
            Serializer = serializer;
        }

        public IHttpRequester HttpRequester { get; set; }
        public ISerializer Serializer { get; set; }

        public virtual async Task<T> RequestAsync<T>(Uri uri) {
            string data = "";
            try {
                data = await HttpRequester.RequestAsync<T>(uri).ConfigureAwait(false);
            }
            catch (WebException e) {
                throw new InvalidRequestException("A request caused a WebException.", e.InnerException as WebException);
            }
            var val = Serializer.Deserialize<T>(data);
            return val;
        }
    }
}