﻿// Copyright 2010 Miguel de Icaza
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using UIKit;
using Foundation;
using System.Text;
using System.Collections.Specialized;
using System.Json;
using System.Xml.Linq;

namespace TweetStation
{
	public enum TweetKind {
		Home,
		Replies,
		Direct,
		Transient,
	}
	
	public partial class TwitterAccount
	{
		// The OAuth configuration for TweetStation
		public static OAuthConfig OAuthConfig = new OAuthConfig () {
			ConsumerKey = "VSemGxR6ZNpo5IWY3dS8uQ",
			TwitPicKey = "e66f585ed2c8be83be12a8f2be9a5981",
			BitlyKey = "R_45898eef7a5772943c2ca54eea9877fd",
			Callback = "http://tirania.org/tweetstation/oauth",
			ConsumerSecret = "MEONRf8QqJDotJWioW1v1sSZVhXlOsTI85xu9eZfJf8",
			RequestTokenUrl = "https://api.twitter.com/oauth/request_token", 
			AccessTokenUrl = "https://twitter.com/oauth/access_token", 
			AuthorizeUrl = "https://twitter.com/oauth/authorize"
		};
		
		const string timelineUri = "https://api.twitter.com/1.1/statuses/home_timeline.json";
		const string mentionsUri = "https://api.twitter.com/1.1/statuses/mentions_timeline.json";
		const string directUri = "https://api.twitter.com/1.1/direct_messages.json";
			
		const string DEFAULT_ACCOUNT = "defaultAccount";
		
		public long AccountId { get; set; }
		public long LastLoaded { get; set; }
		public string Username { get; set; }
		public string OAuthToken { get; set; }
		public string OAuthTokenSecret { get; set; }
		
		static NSString invoker = new NSString ("");
		
		static Dictionary<long, TwitterAccount> accounts = new Dictionary<long, TwitterAccount> ();
		
		public static TwitterAccount CurrentAccount { get; set; }
				
		internal struct Request {
			public string Url;
			public Action<Stream> Callback;
			public bool CallbackOnMainThread;
			
			public Request (string url, bool callbackOnMainThread, Action<Stream> callback)
			{
				Url = url;
				Callback = callback;
				CallbackOnMainThread = callbackOnMainThread;
			}
		}
		
		const int MaxPending = 200;
		static Queue<Request> queue = new Queue<Request> ();
		static int pending;
		
		/// <summary>
		///   Throttled data download from the specified url and invokes the callback with
		///   the resulting data on the main UIKit thread.
		/// </summary>
		/// 
		/// 
		public void Download (string url, Action<Stream> callback)		
		{
			Download (url, true, callback);
		}

		//
		// Downloads the given url, if the callbackOnMainThread is true, this will
		// buffer the result from the server before calling the callback on the main
		// thread.   Otherwise the callback is invoked with a stream that will pull
		// data as it is received from the network and might hang.
		//
		public void Download (string url, bool callbackOnMainThread, Action<Stream> callback)
		{
			lock (queue){				
				pending++;
				if (pending++ < MaxPending)
					Launch (url, callbackOnMainThread, callback);
				else {
					queue.Enqueue (new Request (url, callbackOnMainThread, callback));
				}
			}
		}

#if PRE_OAUTH_CODE
		// In the future, for connecting to non-OAuth twitter sites

		// This is required because by default WebClient wont authenticate
		// until challenged to.   Twitter does not do that, so we need to force
		// the pre-authentication
		class AuthenticatedWebClient : WebClient {
			protected override WebRequest GetWebRequest (Uri address)
			{
				var req = (HttpWebRequest) WebRequest.Create (address);
				req.PreAuthenticate = true;
				
				return req;
			}
		}
#endif
		
		WebClient GetClient ()
		{
			return new WebClient (); 
		}
		
		static void InvokeCallback (Action<Stream> callback, Stream stream)
		{
			try {
				callback (stream);
			} catch  (Exception ex){
				Console.WriteLine (ex);
			}
			if (stream != null)
				stream.Close ();
		}
		
		static string MakeTimelineRequest (TweetKind kind, long? since, long? max_id)
		{
			string uri = null;
			int count = 200;
			switch (kind){
			case TweetKind.Home:
				uri = timelineUri; break;
			case TweetKind.Replies:
				uri = mentionsUri; count = 40; break;
			case TweetKind.Direct:
				uri = directUri; break;
			}
			return uri + "?count=" + count + 
				(since.HasValue ? "&since_id=" + since.Value : "") +
				(max_id.HasValue ? "&max_id=" + max_id.Value : "");
		}
		
		static long lastLaunchTick;
		static object minuteLock = new object ();
		
		void Launch (string url, bool callbackOnMainThread, Action<Stream> callback)
		{
			Util.PushNetworkActive ();
			Uri uri = new Uri (url);
			
			// Wake up 3G if it has been more than 3 minutes
			lock (minuteLock){
				var nowTicks = DateTime.UtcNow.Ticks;
				if (nowTicks-lastLaunchTick > TimeSpan.TicksPerMinute*3)
					ObjCRuntime.Runtime.StartWWAN (uri);
				lastLaunchTick = nowTicks;
			}
			
			var request = (HttpWebRequest) WebRequest.Create (uri);
			request.AutomaticDecompression = DecompressionMethods.GZip;
			request.Headers [HttpRequestHeader.Authorization] = OAuthAuthorizer.AuthorizeRequest (OAuthConfig, OAuthToken, OAuthTokenSecret, "GET", uri, null);
			
			request.BeginGetResponse (ar => {
				try {
					lock (queue)
						pending--;
					Util.PopNetworkActive ();
					Stream stream = null;
					
					try {
						var response = (HttpWebResponse) request.EndGetResponse (ar);
						stream = response.GetResponseStream ();

						// Since the stream will deliver in chunks, make a copy before passing to the main UI
						if (callbackOnMainThread){
							var ms = new MemoryStream ();
							CopyStream (stream, ms);
							ms.Position = 0;
							stream.Close ();
							stream = ms;
						}
					} catch (WebException we){
						var response = we.Response as HttpWebResponse;
						if (response != null){
							switch (response.StatusCode){
								case HttpStatusCode.Unauthorized:
									// This is the case of sharing two keys
									break;
							}

							stream = null;

							using (var exStream = response.GetResponseStream())
							using (StreamReader reader = new StreamReader(exStream))
							{
								Console.WriteLine(reader.ReadToEnd() ?? "Empty body");
							}
						}
						Console.WriteLine (we);
					} catch (Exception e) {
						Console.WriteLine (e);
						stream = null;
					}
					
					if (callbackOnMainThread)
						invoker.BeginInvokeOnMainThread (delegate { InvokeCallback (callback, stream); });
					else 
						InvokeCallback (callback, stream);
					
				} catch (Exception e){
					Console.WriteLine (e);
				}
				lock (queue){
					if (queue.Count > 0){
						var nextRequest = queue.Dequeue ();
						Launch (nextRequest.Url, nextRequest.CallbackOnMainThread, nextRequest.Callback);
					}
				}
			}, null);
		}
		
		// Temporary
		static void CopyStream (Stream source, Stream dest)
		{
			var buffer = new byte [4096];
			int n = 0;
			long total = 0;

			while ((n = source.Read (buffer, 0, buffer.Length)) != 0){
				total += n;
				dest.Write (buffer, 0, n);
			}
		}

		public class Uploader {
			TwitterAccount account;
			public Stream SourceStream;
			public Action<string> UploadCompletedCallback;
			public ProgressHud ProgressHudView;
			bool stop;
			
			public Uploader (TwitterAccount account)
			{
				this.account = account;
			}
			
			public void Cancel ()
			{
				stop = true;
			}
			
			void CopyToEnd (Stream source, Stream dest, Action<long> progressCallback)
			{
				var buffer = new byte [8192];
				int n = 0;
				long total = 0;
	
				while ((n = source.Read (buffer, 0, buffer.Length)) != 0){
					total += n;
					if (stop)
						return;
					
					dest.Write (buffer, 0, n);
					progressCallback (total);
				}
			}
	
			static void AddPart (Stream target, string boundary, bool newline, string header, string value)
			{
				if (newline)
					target.Write (new byte [] { 13, 10 }, 0, 2);
				
				var enc = Encoding.UTF8.GetBytes (String.Format ("--{0}\r\n{1}\r\n\r\n", boundary, header));
				target.Write (enc, 0, enc.Length);
				if (value != null){
					enc = Encoding.UTF8.GetBytes (value);
					target.Write (enc, 0, enc.Length);
				}
			}
			
			//
			// Creates the YFrog form to upload the image
			//
			static Stream GenerateYFrogFrom (string boundary, Stream source, string username)
			{
				var dest = new MemoryStream ();
				AddPart (dest, boundary, false, "Content-Disposition: form-data; name=\"media\"; filename=\"none.png\"\r\nContent-Type: application/octet-stream", null);
				source.Position = 0;
				CopyStream (source, dest);
				AddPart (dest, boundary, true, "Content-Disposition: form-data; name=\"username\"", username);
				var bbytes = Encoding.ASCII.GetBytes (String.Format ("\r\n--{0}--", boundary));
				dest.Write (bbytes, 0, bbytes.Length);
	
				return dest;
			}
			
			float progressValue;
			void SetProgress (float newvalue)
			{
				// We copy this to a global, as the async methods can be invoked out of order
				progressValue = newvalue;
				
				TwitterAccount.invoker.BeginInvokeOnMainThread (delegate {
					// ProgressHudView.Progress = progressValue;
				});
			}
			
			//
			// the progress is reported like this:
			// 10% to open the connection
			// 90% for the image upload
			//
			public void Upload ()
			{
				var boundary = "###" + Guid.NewGuid ().ToString () + "###";
							
				//var url = new Uri ("https://api.twitpic.com/2/upload.json");
				var url = new Uri ("http://yfrog.com/api/xauth_upload");
				var req = (HttpWebRequest) WebRequest.Create (url);
				req.Method = "POST";
				req.ContentType = "multipart/form-data; boundary=" + boundary;
				OAuthAuthorizer.AuthorizeTwitPic (OAuthConfig, req, account.OAuthToken, account.OAuthTokenSecret);
	
				Stream upload = GenerateYFrogFrom (boundary, SourceStream, account.Username);
				req.ContentLength = upload.Length;
				SetProgress (0);
				try {
					using (var rs = req.GetRequestStream ()){
						SetProgress (0.1f);
						upload.Position = 0;
						CopyToEnd (upload, rs, (sofar) => {
							SetProgress (.1f + ((sofar/upload.Length) * 0.9f));
						});
						try {
							rs.Close ();
						} catch {}
					}
				} catch (Exception){
					UploadCompletedCallback (null);
				}
				if (stop)
					return;
				string urlToPic = null;
				try {
					var response = (HttpWebResponse) req.GetResponse  ();
					var stream = response.GetResponseStream ();
					var doc = XDocument.Load (stream);
					if (doc.Element ("rsp").Attribute ("stat").Value == "ok"){
						urlToPic = doc.Element ("rsp").Element ("mediaurl").Value;
					}
					stream.Close ();
				} catch (Exception e){
					Console.WriteLine (e);
				}
				if (stop)
					return;
				
				invoker.BeginInvokeOnMainThread (delegate { 
					//ProgressHudView.Progress = 1;
					UploadCompletedCallback (urlToPic); 
				});
			}
		}
		
		public Uploader UploadPicture (Stream source, Action<string> completed, ProgressHud progressHud)
		{
			return new Uploader (this) { 
				SourceStream = source, 
				UploadCompletedCallback = completed, 
				ProgressHudView = progressHud 
			};
		}
	}
	
	public interface IAccountContainer {
		TwitterAccount Account { get; set; }
	}
}
