using Quobject.EngineIoClientDotNet.ComponentEmitter;
using Quobject.EngineIoClientDotNet.Modules;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using Quobject.EngineIoClientDotNet.Thread;
using System.Threading;
using ModernHttpClient;
using System.Net.Http;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;

namespace Quobject.EngineIoClientDotNet.Client.Transports
{
    public class PollingXHR : Polling
    {
        private XHRRequest sendXhr;

        public PollingXHR(Options options) : base(options)
        {
		}

        protected XHRRequest Request()
        {
            return Request(null);
        }



        protected XHRRequest Request(XHRRequest.RequestOptions opts)
        {
            if (opts == null)
            {
                opts = new XHRRequest.RequestOptions();
            }
            opts.Uri = Uri();
           
            XHRRequest req = new XHRRequest(opts);

            req.On(EVENT_REQUEST_HEADERS, new EventRequestHeadersListener(this)).
                On(EVENT_RESPONSE_HEADERS, new EventResponseHeadersListener(this));


            return req;
        }

        class EventRequestHeadersListener : IListener
        {
            private PollingXHR pollingXHR;

            public EventRequestHeadersListener(PollingXHR pollingXHR)
            {

                this.pollingXHR = pollingXHR;
            }

            public void Call(params object[] args)
            {
                // Never execute asynchronously for support to modify headers.
                pollingXHR.Emit(EVENT_REQUEST_HEADERS, args[0]);
            }

            public int CompareTo(IListener other)
            {
                return this.GetId().CompareTo(other.GetId());
            }

            public int GetId()
            {
                return 0;
            }
        }

        class EventResponseHeadersListener : IListener
        {
            private PollingXHR pollingXHR;

            public EventResponseHeadersListener(PollingXHR pollingXHR)
            {
                this.pollingXHR = pollingXHR;
            }
            public void Call(params object[] args)
            {
                pollingXHR.Emit(EVENT_RESPONSE_HEADERS, args[0]);
            }

            public int CompareTo(IListener other)
            {
                return this.GetId().CompareTo(other.GetId());
            }

            public int GetId()
            {
                return 0;
            }
        }


        protected override void DoWrite(byte[] data, Action action)
        {
            var opts = new XHRRequest.RequestOptions {Method = "POST", Data = data, CookieHeaderValue = Cookie};
            var log = LogManager.GetLogger(Global.CallerName());
            log.Info("DoWrite data = " + data);
            //try
            //{
            //    var dataString = BitConverter.ToString(data);
            //    log.Info(string.Format("DoWrite data {0}", dataString));
            //}
            //catch (Exception e)
            //{
            //    log.Error(e);
            //}

            sendXhr = Request(opts);
            sendXhr.On(EVENT_SUCCESS, new SendEventSuccessListener(action));
            sendXhr.On(EVENT_ERROR, new SendEventErrorListener(this));
            sendXhr.Create();
        }

        class SendEventErrorListener : IListener
        {
            private PollingXHR pollingXHR;

            public SendEventErrorListener(PollingXHR pollingXHR)
            {
                this.pollingXHR = pollingXHR;
            }          

            public void Call(params object[] args)
            {
                Exception err = args.Length > 0 && args[0] is Exception ? (Exception) args[0] : null;
                pollingXHR.OnError("xhr post error", err);
            }

            public int CompareTo(IListener other)
            {
                return this.GetId().CompareTo(other.GetId());
            }

            public int GetId()
            {
                return 0;
            }
        }

        class SendEventSuccessListener : IListener
        {
            private Action action;

            public SendEventSuccessListener(Action action)
            {
                this.action = action;
            }

            public void Call(params object[] args)
            {
                action();
            }

            public int CompareTo(IListener other)
            {
                return this.GetId().CompareTo(other.GetId());
            }

            public int GetId()
            {
                return 0;
            }
        }


        protected override void DoPoll()
        {
            var log = LogManager.GetLogger(Global.CallerName());
            log.Info("xhr DoPoll");
            var opts = new XHRRequest.RequestOptions { CookieHeaderValue = Cookie };
            sendXhr = Request(opts);
            sendXhr.On(EVENT_DATA, new DoPollEventDataListener(this));
            sendXhr.On(EVENT_ERROR, new DoPollEventErrorListener(this));

            sendXhr.Create();
        }

        class DoPollEventDataListener : IListener
        {
            private PollingXHR pollingXHR;

            public DoPollEventDataListener(PollingXHR pollingXHR)
            {
                this.pollingXHR = pollingXHR;
            }
           

            public void Call(params object[] args)
            {
                object arg = args.Length > 0 ? args[0] : null;
                if (arg is string)
                {
                    pollingXHR.OnData((string)arg);
                }
                else if (arg is byte[])
                {
                    pollingXHR.OnData((byte[])arg);
                }
            }

            public int CompareTo(IListener other)
            {
                return this.GetId().CompareTo(other.GetId());
            }

            public int GetId()
            {
                return 0;
            }

        }

        class DoPollEventErrorListener : IListener
        {
            private PollingXHR pollingXHR;

            public DoPollEventErrorListener(PollingXHR pollingXHR)
            {
                this.pollingXHR = pollingXHR;
            }

            public void Call(params object[] args)
            {
                Exception err = args.Length > 0 && args[0] is Exception ? (Exception)args[0] : null;
                pollingXHR.OnError("xhr poll error", err);
            }

            public int CompareTo(IListener other)
            {
                return this.GetId().CompareTo(other.GetId());
            }

            public int GetId()
            {
                return 0;
            }
        }


        public class XHRRequest : Emitter
        {
            private string Method;
            private string Uri;
            private byte[] Data;
            private string CookieHeaderValue;
			private static HttpClient Xhr;
			private Timer _xhrRequestTimer;
			private TimerCallback _xhrRequestAction;

            public XHRRequest(RequestOptions options)
            {
                Method = options.Method ?? "GET";
                Uri = options.Uri;
                Data = options.Data;
                CookieHeaderValue = options.CookieHeaderValue;
            }

            public void Create()
            {

				var log = LogManager.GetLogger(Global.CallerName());

				//_xhrRequestAction = delegate {
				if (Xhr == null) {
					log.Info(string.Format("Creating a new HttpClient"));
					var nativeMessageHandler = new NativeMessageHandler ();
					Xhr = new HttpClient (nativeMessageHandler);
				}
	            
				log.Info(string.Format("xhr open {0}: {1}", Method, Uri));
				if (Method == "POST") {
					//Xhr.DefaultRequestHeaders.TransferEncodingChunked = true;
					PostRequest (log);
				} else {
					//Xhr.DefaultRequestHeaders.TransferEncodingChunked = false;
					GetRequest (log);
				}

				//};
				//_xhrRequestTimer = new Timer(_xhrRequestAction, null, 1000); 

				/*
                try
                {
                    log.Info(string.Format("xhr open {0}: {1}", Method, Uri));
					Xhr = new HttpClient(new NativeMessageHandler());

                    Xhr = (HttpWebRequest) WebRequest.Create(Uri);
                    Xhr.Method = Method;
                    if (CookieHeaderValue != null)
                    {
						Xhr.Headers["Cookie"] = CookieHeaderValue;
                        //Xhr.Headers.Add("Cookie", CookieHeaderValue);                        
                    }
                }
                catch (Exception e)
                {
                    log.Error(e);
                    OnError(e);
                    return;
                }

				_xhrRequestAction = delegate {
					if (Method == "POST") {
						Xhr.ContentType = "application/octet-stream";
						PostRequest (log);
					} else {
						Xhr.ContentType = "text/xml; encoding='utf-8'";
						GetRequest (log);
					}
				};
				_xhrRequestTimer = new Timer(_xhrRequestAction, null, 2500); 
				*/
            }

			private HttpResponseMessage PostAsyncSafe(HttpClient client, string requestUri, HttpContent content, LogManager log)
			{
				return PerformActionSafe(() => (client.PostAsync(requestUri, content)).Result, log);
			}

			private HttpResponseMessage PerformActionSafe(Func<HttpResponseMessage> action, LogManager log)
			{
				try
				{
					return action();
				}
				catch (AggregateException aex)
				{ 
					log.Error ("PerformActionSafe PostAsyncSafe ", aex);
					OnError (aex);

					Exception firstException = null;
					if (aex.InnerExceptions != null && aex.InnerExceptions.Any())
					{
						firstException = aex.InnerExceptions.First();

						if (firstException.InnerException != null)
							firstException = firstException.InnerException;
					}

					log.Error ("firstException ", firstException);

					var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
					{
						Content =
							new StringContent(firstException != null
								? firstException.ToString()
								: "Encountered an AggreggateException without any inner exceptions")
						};

					return null;
				}
			}

			private void PostRequest (LogManager log)
			{
				var log2 = LogManager.GetLogger (Global.CallerName ());
				log.Info ("***** BeginPostRequestStream  *****");
				/* Sending Data to Server. */

				var cookieContainer = new CookieContainer();

					if (CookieHeaderValue != null) {
						//cookieContainer.SetCookies (new Uri (Uri), CookieHeaderValue);
					}

					/*
					if (Method == "POST") {
						Xhr.ContentType = "application/octet-stream";
					} else {
						Xhr.ContentType = "text/xml; encoding='utf-8'";
					}
					*/

					var dataContent = new ByteArrayContent (Data);
					dataContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); 

					//using (HttpResponseMessage response = PostAsyncSafe (Xhr, Uri, dataContent, log)) {
					//HttpResponseMessage response = Xhr.PostAsync (Uri, dataContent).Result;
					//Xhr.PostAsync (Uri, dataContent).ContinueWith (requestTask => {
					var formContent = new FormUrlEncodedContent(new[] 
					{
						new KeyValuePair<string, string>("", "")
					});

					HttpResponseMessage response = null;
					try{
						//response = Xhr.PostAsync(Uri, dataContent).Result;
						var httpRequestMessage = new HttpRequestMessage (HttpMethod.Post, new Uri(Uri));
						httpRequestMessage.Content = dataContent;
						response = Xhr.SendAsync (httpRequestMessage,HttpCompletionOption.ResponseContentRead | HttpCompletionOption.ResponseHeadersRead).Result;
					} catch (Exception e){
						log.Error ("PostAsync", e);
						log.Info (String.Format("response: {0}", response));
						OnError (e);
						return;
					}
					
					/*
						if (response == null) {
							return;
						}*/

					try {
						response.EnsureSuccessStatusCode ();
					} catch (Exception e) {
						log.Error ("EnsureSuccessStatusCode in POST call:", e);
						OnError (e);
					}


					log.Info ("Xhr.PostResponse ");
					/*
					var responseHeaders = new Dictionary<string, string> ();
					foreach (KeyValuePair<string, IEnumerable<string>> entry in response.Headers) {
						var value = String.Join(", ", entry.Value.ToArray());
						responseHeaders.Add (entry.Key, value);
						log.Info (String.Format ("Response Headers; Key: {0}, Value: {1}", entry.Key, value));
					}
					OnResponseHeaders (responseHeaders);
					*/
					//string contentType = response.Headers ["Content-Type"];
					//log.Info (String.Format("Content-Type: {0}", contentType));

					HttpContent content = response.Content;
					//string result =  content.ReadAsStringAsync();

					string contentType = content.GetType ().FullName;
					log.Info (String.Format ("Content-Type: {0}", contentType));

					string a = content.ReadAsStringAsync ().Result;
					log.Info (String.Format ("post result array: {0}", a));
					OnData (a);

					//});
						/*
							//using (var resStream = response.GetResponseStream ()) {
							Debug.Assert (content != null, "resStream != null");
								if (contentType.Equals ("application/octet-stream", StringComparison.OrdinalIgnoreCase)) {
									
								var a = content.ReadAsByteArrayAsync ();
								log.Info (String.Format ("post result array: {0}", a));
								OnData (a);
									/*
									var buffer = new byte[16 * 1024];
									using (var ms = new MemoryStream ()) {
										int read;
										while ((read = resStream.Read (buffer, 0, buffer.Length)) > 0) {
											ms.Write (buffer, 0, read);
										}
										var a = ms.ToArray ();
										log.Info (String.Format ("post result array: {0}", a));
										OnData (a);
									}
									*

								} else {

								var a = content.ReadAsStringAsync ();
								log.Info (String.Format ("post result array: {0}", a));
								OnData (a);

									/*
									using (var sr = new StreamReader (resStream)) {
										var result = sr.ReadToEnd ();
										log.Info (String.Format ("post esult: {0}", result));
										OnData (result);
									}*


								}
							//}
							*/						
					
				

				/*
				Xhr.BeginGetRequestStream (asynchResultReq =>  {
					try {
						using (var requestStream = Xhr.EndGetRequestStream (asynchResultReq)) {
							// Write to the request stream.
							log2.Info(String.Format("******* Writing: {0}, Data:{1}",requestStream.CanWrite,Data));

							requestStream.WriteAsync (Data, 0, Data.Length);
							requestStream.Dispose ();
						}
					}
					catch (Exception ex) {
						var errorString = String.Format ("ERROR BeginGetRequestStream: '{0}' when making {1} request to {2}", ex.Message, Xhr.Method, Xhr.RequestUri.AbsoluteUri);
						log.Info (errorString);
						log.Error ("Create call failed", ex);
						OnError (ex);
					}

					log.Info ("Task.Run Create start");

					// Requesting Response from Server. 
					Xhr.BeginGetResponse (asynchResultResp =>  {
						try {
							using (var res = Xhr.EndGetResponse (asynchResultResp)) {
								log.Info ("Xhr.GetResponse ");
								var responseHeaders = new Dictionary<string, string> ();
								foreach (string key in res.Headers.AllKeys) {
									string value = res.Headers [key];
									responseHeaders.Add (key, value);
								}
								OnResponseHeaders (responseHeaders);
								var contentType = res.Headers ["Content-Type"];
								using (var resStream = res.GetResponseStream ()) {
									Debug.Assert (resStream != null, "resStream != null");
									if (contentType.Equals ("application/octet-stream", StringComparison.OrdinalIgnoreCase)) {
										var buffer = new byte[16 * 1024];
										using (var ms = new MemoryStream ()) {
											int read;
											while ((read = resStream.Read (buffer, 0, buffer.Length)) > 0) {
												ms.Write (buffer, 0, read);
											}
											var a = ms.ToArray ();
											log.Info (String.Format("post result array: {0}", a));
											OnData (a);
										}
									}
									else {
										using (var sr = new StreamReader (resStream)) {
											var result = sr.ReadToEnd ();
											log.Info (String.Format("post esult: {0}", result));
											OnData (result);
										}
									}
								}
							}
						}
						catch (Exception ex) {
							var errorString = String.Format ("ERROR BeginGetResponse: '{0}' when making {1} request to {2}", ex.Message, Xhr.Method, Xhr.RequestUri.AbsoluteUri);
							log.Info (errorString);
							log.Error ("Create call failed", ex);
							OnError (ex);
						}
					}, null);
					log2.Info ("Task.Run Create finish");
				}, null);
				*/
			}


			private void GetRequest (LogManager log)
			{

				log.Info("***** BeginGet Response *****");
				// Requesting Response from Server.

					var cookieContainer = new CookieContainer();
					//using (var Xhr = new HttpClient(new NativeMessageHandler(){ CookieContainer = cookieContainer, UseCookies = true }))
					//{
						//var request = new HttpRequestMessage(HttpMethod.Get, Uri);
						//request.Content = new StringContent(Serialize(obj), Encoding.UTF8, "text/xml");
						//var response =  Xhr.SendAsync(request);
						//return  await response.Content.ReadAsStringAsync();

						//Xhr.Timeout = TimeSpan.FromMinutes(10);

						if (CookieHeaderValue != null)
						{
							//cookieContainer.SetCookies (new Uri(Uri), CookieHeaderValue);
						}

						HttpResponseMessage response = Xhr.GetAsync (Uri).Result;

						try {
							response.EnsureSuccessStatusCode ();
						} catch (Exception e) {
							log.Error ("EnsureSuccessStatusCode in GET call:", e);
							OnError (e);
						}

						log.Info ("Xhr.GetResponse ");
						var responseHeaders = new Dictionary<string, string> ();
						foreach (KeyValuePair<string, IEnumerable<string>> entry in response.Headers) {
							string value = entry.Value.SingleOrDefault<string> ();
							responseHeaders.Add (entry.Key, value);
							log.Info (String.Format ("Response Headers; Key: {0}, Value: {1}", entry.Key, value));
						}
						OnResponseHeaders (responseHeaders);


						HttpContent content = response.Content;
							//string result =  content.ReadAsStringAsync();

								string contentType = content.GetType ().ToString();
							log.Info (String.Format ("Content-Type: {0}", contentType));

							string a = content.ReadAsStringAsync ().Result;
							log.Info (String.Format ("get result array: {0}", a));
							OnData (a);

							/*
						//using (var resStream = response.GetResponseStream ()) {
						Debug.Assert (content != null, "resStream != null");
						if (contentType.Equals ("application/octet-stream", StringComparison.OrdinalIgnoreCase)) {

							var a = content.ReadAsByteArrayAsync ();
							log.Info (String.Format ("post result array: {0}", a));
							OnData (a);
							/*
									var buffer = new byte[16 * 1024];
									using (var ms = new MemoryStream ()) {
										int read;
										while ((read = resStream.Read (buffer, 0, buffer.Length)) > 0) {
											ms.Write (buffer, 0, read);
										}
										var a = ms.ToArray ();
										log.Info (String.Format ("post result array: {0}", a));
										OnData (a);
									}
									*

						} else {

							var a = content.ReadAsStringAsync ();
							log.Info (String.Format ("post result array: {0}", a));
							OnData (a);

							/*
									using (var sr = new StreamReader (resStream)) {
										var result = sr.ReadToEnd ();
										log.Info (String.Format ("post esult: {0}", result));
										OnData (result);
									}*


						}
						//}
						*/

				//}

				/*
				Xhr.BeginGetResponse (asynchResultResp =>  {
					try {
						using (var res = Xhr.EndGetResponse (asynchResultResp)) {

							var responseHeaders = new Dictionary<string, string> ();
							foreach (string key in res.Headers.AllKeys) {
								string value = res.Headers [key];
								responseHeaders.Add (key, value);
							}
							OnResponseHeaders (responseHeaders);
							var contentType = res.Headers ["Content-Type"];
							using (var resStream = res.GetResponseStream ()) {

								Debug.Assert (resStream != null, "resStream != null");
								if (contentType.Equals ("application/octet-stream", StringComparison.OrdinalIgnoreCase)) {
									log.Info ("it's equal ");
									var buffer = new byte[16 * 1024];
									using (var ms = new MemoryStream ()) {
										int read;
										while ((read = resStream.Read (buffer, 0, buffer.Length)) > 0) {
											ms.Write (buffer, 0, read);
										}
										var a = ms.ToArray ();
										log.Info(String.Format("get result array: {0}", a));
										OnData (a);
									}
								}
								else {
									using (var sr = new StreamReader (resStream)) {
										var result = sr.ReadToEnd ();
										log.Info (String.Format("get result: {0}", result));
										OnData (result);
									}
								}
							}
						}
					}
					catch (Exception ex) {
						var errorString = String.Format ("ERROR Requesting Stream: '{0}' when making {1} request to {2}", ex.Message, Xhr.Method, Xhr.RequestUri.AbsoluteUri);
						log.Info (errorString);
						log.Error ("Create call failed", ex);
						OnError (ex);
					}
				}, null);
				*/
			}
				
            private void OnSuccess()
            {
                this.Emit(EVENT_SUCCESS);
            }

            private void OnData(string data)
            {
                var log = LogManager.GetLogger(Global.CallerName());
                log.Info("OnData string = " + data);
                this.Emit(EVENT_DATA, data);
                this.OnSuccess();
            }

            private void OnData(byte[] data)
            {
                var log = LogManager.GetLogger(Global.CallerName());
				log.Info(String.Format("OnData byte[] ={0}", UTF8Encoding.UTF8.GetString(data, 0, data.Length)));
                this.Emit(EVENT_DATA, data);
                this.OnSuccess();
            }

            private void OnError(Exception err)
            {
                this.Emit(EVENT_ERROR, err);
            }

            private void OnRequestHeaders(Dictionary<string, string> headers)
            {
                this.Emit(EVENT_REQUEST_HEADERS, headers);
            }

            private void OnResponseHeaders(Dictionary<string, string> headers)
            {
                this.Emit(EVENT_RESPONSE_HEADERS, headers);
            }

            public class RequestOptions
            {
                public string Uri;
                public string Method;
                public byte[] Data;
                public string CookieHeaderValue;
            }
        }



    }

	internal delegate void TimerCallback(object state);
	internal sealed class Timer : CancellationTokenSource, IDisposable
	{
		internal Timer(TimerCallback callback, object state, int dueTime)
		{
			Task.Delay(dueTime, Token).ContinueWith((t, s) =>
				{
					var tuple = (Tuple<TimerCallback, object>)s;
					tuple.Item1(tuple.Item2);
				}, Tuple.Create(callback, state), CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
				TaskScheduler.Default);
		}

		public new void Dispose() { Cancel(); }
	}

}
