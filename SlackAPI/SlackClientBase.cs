using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SlackAPI
{
    public abstract class SlackClientBase
    {
        protected readonly IWebProxy proxySettings;
        private readonly HttpClient httpClient;
        public string APIBaseLocation { get; set; } = "https://slack.com/api/";

        static SlackClientBase()
        {
            // Force Tls 1.2 for Slack
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        protected SlackClientBase()
        {
            this.httpClient = new HttpClient();
        }

        protected SlackClientBase(IWebProxy proxySettings)
        {
            this.proxySettings = proxySettings;
            this.httpClient = new HttpClient(new HttpClientHandler { UseProxy = true, Proxy = proxySettings });
        }

        protected Uri GetSlackUri(string path, Tuple<string, string>[] getParameters)
        {
            string parameters = default;

            if (getParameters != null && getParameters.Length > 0)
            {
                parameters = getParameters
                .Where(x => x.Item2 != null)
                .Select(new Func<Tuple<string, string>, string>(a =>
                {
                    try
                    {
                        return string.Format("{0}={1}", Uri.EscapeDataString(a.Item1), Uri.EscapeDataString(a.Item2));
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(string.Format("Failed when processing '{0}'.", a), ex);
                    }
                }))
                .Aggregate((a, b) =>
                {
                    if (string.IsNullOrEmpty(a))
                        return b;
                    else
                        return string.Format("{0}&{1}", a, b);
                });

            }

            Uri requestUri = default;

            if (!string.IsNullOrEmpty(parameters))
                requestUri = new Uri(string.Format("{0}?{1}", path, parameters));
            else
                requestUri = new Uri(path);

            return requestUri;
        }

    private HttpWebRequest GetAPIWebRequest<K>(Tuple<string, string>[] getParameters)
        where K : Response
    {
      RequestPath path = RequestPath.GetRequestPath<K>();
      //TODO: Custom paths? Appropriate subdomain paths? Not sure.
      //Maybe store custom path in the requestpath.path itself?

      Uri requestUri = GetSlackUri(Path.Combine(APIBaseLocation, path.Path), getParameters);
      return CreateWebRequest(requestUri);
    }

    private Tuple<string, string>[] ApplyRequestAuth(HttpWebRequest request, Tuple<string, string>[] postParameters, string token, CookieContainer cookies)
    {
      var actualPostParameters = (Tuple<string, string>[])postParameters.Clone();

      // if cookies are specified then the token gets passed in as a parameter, not as part of the header
      if (cookies != null)
      {
        request.CookieContainer = cookies;

        Array.Resize(ref actualPostParameters, actualPostParameters.Length + 1);

        actualPostParameters[actualPostParameters.Length - 1] = new Tuple<string, string>("token", token);

      }
      else if (!string.IsNullOrEmpty(token))
      {
        request.Headers.Add("Authorization", "Bearer " + token);
      }

      return actualPostParameters;
    }

    protected void APIRequest<K>(Action<K> callback, Tuple<string, string>[] getParameters, Tuple<string, string>[] postParameters, string token = "", CookieContainer cookies = null)
        where K : Response
        {

            var request = GetAPIWebRequest<K>(getParameters);
            var actualPostParameters = ApplyRequestAuth(request, postParameters, token, cookies);

            //This will handle all of the processing.
            RequestState<K> state = new RequestState<K>(request, actualPostParameters, callback);
            state.Begin();
        }

        public Task<K> APIRequestAsync<K>(Tuple<string, string>[] getParameters, Tuple<string, string>[] postParameters, string token = "", CookieContainer cookies = null)
            where K : Response
        {
            var request = GetAPIWebRequest<K>(getParameters);
            var actualPostParameters = ApplyRequestAuth(request, postParameters, token, cookies);

            //This will handle all of the processing.
            var state = new RequestStateForTask<K>(request, actualPostParameters);
            return state.Execute();
        }

    protected void APIGetRequest<K>(Action<K> callback, params Tuple<string, string>[] getParameters)
            where K : Response
        {
            APIRequest<K>(callback, getParameters, new Tuple<string, string>[0]);
        }

        public Task<K> APIGetRequestAsync<K>(params Tuple<string, string>[] getParameters)
            where K : Response
        {
            return APIRequestAsync<K>(getParameters, new Tuple<string, string>[0]);
        }

        protected HttpWebRequest CreateWebRequest(Uri requestUri)
        {
            var httpWebRequest = (HttpWebRequest)HttpWebRequest.Create(requestUri);
            if (proxySettings != null)
            {
                httpWebRequest.Proxy = this.proxySettings;
            }

            return httpWebRequest;
        }

        protected Task<HttpResponseMessage> PostRequestAsync(string requestUri, MultipartFormDataContent form, string token)
        {
            var requestMessage = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                Content = form,
                RequestUri = new Uri(requestUri),
            };

            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return httpClient.SendAsync(requestMessage);
        }

        public void RegisterConverter(JsonConverter converter)
        {
            if (converter == null)
            {
                throw new ArgumentNullException("converter");
            }

            Extensions.Converters.Add(converter);
        }
    }
}
