﻿// Composer.cs:
//    Views and ViewControllers for composing messages
//
// Copyright 2010 Miguel de Icaza
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
//
using System;
using CoreGraphics;
using System.Linq;
using System.Text;
using System.Web;
using Foundation;
using UIKit;
using CoreLocation;
using SQLite;
using System.IO;
using System.Net;
using AVFoundation;
using System.Text.RegularExpressions;
using System.Threading;

namespace TweetStation
{
	public class ComposerView : UIView {
		const UIBarButtonItemStyle style = UIBarButtonItemStyle.Bordered;
		internal UITextView textView;
		Composer composer;
		UIToolbar toolbar;
		UILabel charsLeft;
		internal UIBarButtonItem GpsButtonItem, ShrinkItem;
		public event Action LookupUserRequested;
		public bool justShrank;
		
		public ComposerView (CGRect bounds, Composer composer, EventHandler cameraTapped) : base (bounds)
		{
			this.composer = composer;
			textView = new UITextView (CGRect.Empty) {
				Font = UIFont.SystemFontOfSize (18),
			};
			
			// Work around an Apple bug in the UITextView that crashes
			if (ObjCRuntime.Runtime.Arch == ObjCRuntime.Arch.SIMULATOR)
				textView.AutocorrectionType = UITextAutocorrectionType.No;
			
			textView.Changed += HandleTextViewChanged;

			charsLeft = new UILabel (CGRect.Empty) { 
				Text = "140", 
				TextColor = UIColor.White,
				BackgroundColor = UIColor.Clear,
				TextAlignment = UITextAlignment.Right
			};

			toolbar = new UIToolbar (CGRect.Empty);
			GpsButtonItem = new UIBarButtonItem (UIImage.FromBundle ("Images/gps.png"), style, InsertGeo);
			ShrinkItem = new UIBarButtonItem (UIImage.FromBundle ("Images/arrows.png"), style, OnShrinkTapped);
			
			toolbar.SetItems (new UIBarButtonItem [] {
				new UIBarButtonItem (UIBarButtonSystemItem.Trash, delegate { textView.Text = ""; } ) { Style = style },
				new UIBarButtonItem (UIBarButtonSystemItem.FlexibleSpace, null),
				ShrinkItem,
				new UIBarButtonItem (UIBarButtonSystemItem.Search, delegate { if (LookupUserRequested != null) LookupUserRequested (); }) { Style = style },
				new UIBarButtonItem (UIBarButtonSystemItem.Camera, cameraTapped) { Style = style },
				GpsButtonItem }, false);	

			AddSubview (toolbar);
			AddSubview (textView);
			AddSubview (charsLeft);
		}
		
		void HandleTextViewChanged (object sender, EventArgs e)
		{
			string text = textView.Text;
			
			var enabled = composer.sendItem.Enabled;
			if (enabled ^ (text.Length != 0))
			    composer.sendItem.Enabled = !enabled;
			
			var left = 140-text.Length;
			if (left < 0)
				charsLeft.TextColor = UIColor.Red;
			else
				charsLeft.TextColor = UIColor.White;
			
			charsLeft.Text = (140-text.Length).ToString ();
		}
		
		internal void OnShrinkTapped (object sender, EventArgs args)
		{
			// Double tapping on the shrink link removes vowels, idea from Nat.
			// you -> u
			// vowels removed
			// and -> &
			if (justShrank){
				var copy = Regex.Replace (textView.Text, "\\band\\b", "&", RegexOptions.IgnoreCase);
				copy = Regex.Replace (copy, "\\byou\\b", "\u6666", RegexOptions.IgnoreCase);
				copy = Regex.Replace (copy, "\\B[aeiou]\\B", "");
				copy = copy.Replace ("\u6666", " u ");
				textView.Text = copy;
				
				// Hack because the text changed event is not raised
				// synchronously, but after the UI pumps for events
				justShrank = false;
				return;
			}
			
			var words = textView.Text.Split (new char [] { ' '}, StringSplitOptions.RemoveEmptyEntries);
			
			foreach (var word in words)
				if (word.StartsWith ("http://") || word.StartsWith ("https://")){
					ShrinkUrls (words);
					break;
				}
			
			textView.Text = String.Join (" ", words);
			justShrank = true;
		}

		// Need HUD display here
		void ShrinkUrls (string [] words)
		{
			var hud = new LoadingHUDView (Locale.GetText ("Shrinking"));
			this.AddSubview (hud);
			hud.StartAnimating ();
			
			var wc = new WebClient ();
			for (int i = 0; i < words.Length; i++){
				if (!words [i].StartsWith ("http://") && !words [i].StartsWith ("https://"))
				    continue;
				    
				try {
					words [i] = wc.DownloadString (new Uri ("http://is.gd/api.php?longurl=" + HttpUtility.UrlEncode (words [i])));
				} catch {
				}
			}
			hud.StopAnimating ();
			hud.RemoveFromSuperview ();
			hud = null;
		}
		
		internal void InsertGeo (object sender, EventArgs args)
		{
			GpsButtonItem.Enabled = false;
			Util.RequestLocation (newLocation => {
				composer.location = newLocation;
				GpsButtonItem.Enabled = true;
			});
		}
		
		internal void Reset (string text)
		{
			textView.Text = text;
			justShrank = false;
			HandleTextViewChanged (null, null);
		}
		
		public override void LayoutSubviews ()
		{
			Resize (Bounds);
		}
		
		void Resize (CGRect bounds)
		{
			textView.Frame = new CGRect (0, 0, bounds.Width, bounds.Height-44);
			toolbar.Frame = new CGRect (0, bounds.Height-44, bounds.Width, 44);
			charsLeft.Frame = new CGRect (64, bounds.Height-44, 50, 44);
		}
		
		public string Text { 
			get {
				return textView.Text;
			}
			set {
				textView.Text = value;
			}
		}
	}
	
	/// <summary>
	///   Composer is a singleton that is shared through the lifetime of the app,
	///   the public methods in this class reset the values of the composer on 
	///   each invocation.
	/// </summary>
	public class Composer : UIViewController
	{
		ComposerView composerView;
		UINavigationBar navigationBar;
		UINavigationItem navItem;
		internal UIBarButtonItem sendItem;
		UIViewController previousController;
		long InReplyTo;
		string directRecipient;
		internal CLLocation location;
		AudioPlay player;
		ProgressHud progressHud;
		bool FromLibrary;
		UIImage Picture;
		
		public static readonly Composer Main = new Composer ();
		
		Composer () : base (null, null)
		{
			// Navigation Bar
			navigationBar = new UINavigationBar (new CGRect (0, 0, 320, 44));
			navItem = new UINavigationItem ("");
			var close = new UIBarButtonItem (Locale.GetText ("Close"), UIBarButtonItemStyle.Plain, CloseComposer);
			navItem.LeftBarButtonItem = close;
			sendItem = new UIBarButtonItem (Locale.GetText ("Send"), UIBarButtonItemStyle.Plain, PostCallback);
			navItem.RightBarButtonItem = sendItem;

			navigationBar.PushNavigationItem (navItem, false);
			
			// Composer
			composerView = new ComposerView (ComputeComposerSize (CGRect.Empty), this, CameraTapped);
			composerView.LookupUserRequested += delegate {
				PresentModalViewController (new UserSelector (userName => {
					composerView.Text += ("@" + userName + " ");
				}), true);
			};
			
			// Add the views
			UIKeyboard.Notifications.ObserveWillShow(KeyboardWillShow);

			View.AddSubview (composerView);
			View.AddSubview (navigationBar);
		}

		void CameraTapped (object sender, EventArgs args)
		{
			if (Picture == null){
				TakePicture ();
				return;
			}
			
			var sheet = Util.GetSheet (Locale.GetText ("Tweet already contains a picture"));
			sheet.AddButton (Locale.GetText ("Discard Picture"));
			sheet.AddButton (Locale.GetText ("Pick New Picture"));
			sheet.AddButton (Locale.GetText ("Cancel"));
			
			sheet.CancelButtonIndex = 2;
			sheet.Clicked += delegate(object ss, UIButtonEventArgs e) {
				if (e.ButtonIndex == 2)
					return;
				
				if (e.ButtonIndex == 0)
					Picture = null;
				else
					TakePicture ();
			};
			sheet.ShowInView (AppDelegate.MainAppDelegate.MainView);

		}
		
		void TakePicture ()
		{
			FromLibrary = true;
			if (!UIImagePickerController.IsSourceTypeAvailable (UIImagePickerControllerSourceType.Camera)){
				Camera.SelectPicture (this, PictureSelected);
				return;
			}
			
			var sheet = Util.GetSheet ("");
			sheet.AddButton (Locale.GetText ("Take a photo or video"));
			sheet.AddButton (Locale.GetText ("From Album"));
			sheet.AddButton (Locale.GetText ("Cancel"));
			
			sheet.CancelButtonIndex = 2;
			sheet.Clicked += delegate(object sender, UIButtonEventArgs e) {
				if (e.ButtonIndex == 2)
					return;
				
				if (e.ButtonIndex == 0){
					FromLibrary = false;
					Camera.TakePicture (this, PictureSelected);
				} else
					Camera.SelectPicture (this, PictureSelected);
			};
			sheet.ShowInView (AppDelegate.MainAppDelegate.MainView);
		}

		UIImage Scale (UIImage image, CGSize size)
		{
			UIGraphics.BeginImageContext (size);
			image.Draw (new CGRect (new CGPoint (0, 0), size));
			var ret = UIGraphics.GetImageFromCurrentImageContext ();
			UIGraphics.EndImageContext ();
			return ret;
		}
		
		void PictureSelected (NSDictionary pictureDict)
		{
			nint level = Util.Defaults.IntForKey ("sizeCompression");
			
			if ((pictureDict [UIImagePickerController.MediaType] as NSString) == "public.image"){
				Picture = pictureDict [UIImagePickerController.EditedImage] as UIImage;
				if (Picture == null)
					Picture = pictureDict [UIImagePickerController.OriginalImage] as UIImage;
				
				// Save a copy of the original picture
				if (!FromLibrary){
					Picture.SaveToPhotosAlbum (delegate {
						// ignore errors
					});
				}
				
				var size = Picture.Size;
				nfloat maxWidth;
				switch (level){
				case 0:
					maxWidth = 640;
					break;
				case 1:
					maxWidth = 1024;
					break;
				default:
					maxWidth = size.Width;
					break;
				}

				var hud = new LoadingHUDView (Locale.GetText ("Image"), Locale.GetText ("Compressing"));
				View.AddSubview (hud);
				hud.StartAnimating ();
				
				// Show the UI, and on a callback, do the scaling, so the user gets an animation
				NSTimer.CreateScheduledTimer (TimeSpan.FromSeconds (0), delegate {
					if (size.Width > maxWidth || size.Height > maxWidth)
						Picture = Scale (Picture, new CGSize (maxWidth, maxWidth*size.Height/size.Width));
					hud.StopAnimating ();
					hud.RemoveFromSuperview ();
					hud = null;
				});
			} else {
				//NSUrl movieUrl = pictureDict [UIImagePickerController.MediaURL] as NSUrl;
				
				// Future use, when we find a video host that does not require your Twitter login/password
			}
			
			pictureDict.Dispose ();
		}
		
		public void ReleaseResources ()
		{
		}
		
		public override void ViewDidLoad ()
		{
			base.ViewDidLoad ();			
		}

		public void ResetComposer (string caption, string initialText)
		{
			composerView.Reset (initialText);
			InReplyTo = 0;
			directRecipient = null;
			location = null;
			composerView.GpsButtonItem.Enabled = true;
			navItem.Title = caption;
		}
		
		void CloseComposer (object sender, EventArgs a)
		{
			if (Picture != null){
				Picture.Dispose ();
				Picture = null;
			}
			
			sendItem.Enabled = true;
			previousController.DismissModalViewController (true);
			if (player != null)
				player.Stop ();
		}
		
		void AppendLocation (StringBuilder content)
		{
			if (location == null)
				return;

			// TODO: check if geo_enabled is set for the user, if not, open a browser to have the user change this.
			content.AppendFormat ("&lat={0}&long={1}", location.Coordinate.Latitude, location.Coordinate.Longitude);
		}
		
		void PostCallback (object sender, EventArgs a)
		{
			sendItem.Enabled = false;
			
			if (Picture == null){
				Post ();
				return;
			}
			
			var jpeg = Picture.AsJPEG ();
			Stream stream;
			unsafe { stream = new UnmanagedMemoryStream ((byte*) jpeg.Bytes, (long)jpeg.Length); }
			
			progressHud = new ProgressHud (Locale.GetText ("Uploading Image"), Locale.GetText ("Stop"));
			var uploader = TwitterAccount.CurrentAccount.UploadPicture (stream, PicUploadComplete, progressHud);
	
			progressHud.ButtonPressed += delegate { 
				uploader.Cancel (); 
				DestroyProgressHud ();
			};
			View.AddSubview (progressHud);
			ThreadPool.QueueUserWorkItem (delegate {
				uploader.Upload ();
				
				// This captures the variable and handle of jpeg, and then we clear it
				// to release it
				jpeg = null;
			});
		}
		
		void DestroyProgressHud ()
		{
			progressHud.RemoveFromSuperview ();
			progressHud = null;
		}

		void PicUploadComplete (string name)
		{
			DestroyProgressHud ();
			
			if (name == null){
				var alert = UIAlertController.Create (Locale.GetText ("Error"), 
	            	Locale.GetText ("There was an error uploading the media, do you want to post without it?"),
					UIAlertControllerStyle.Alert);
				alert.AddAction(UIAlertAction.Create(Locale.GetText("Cancel Post"), UIAlertActionStyle.Cancel, null));
				alert.AddAction(UIAlertAction.Create(Locale.GetText("Post"), UIAlertActionStyle.Default, (alertAction) =>
				{
					Post();
				}));
				this.PresentViewController(alert, true, null);
			} else {
				var text = composerView.Text.Trim ();
				if (text.Length + name.Length > 140){
					var alert = UIAlertController.Create("Error",
						Locale.GetText ("Message is too long"),
						UIAlertControllerStyle.Alert);
					alert.AddAction(UIAlertAction.Create("Ok", UIAlertActionStyle.Default, null));
                    this.PresentViewController(alert, true, null);
				} else {
					text = text + " " + name;
					if (text.Length > 140)
						text = text.Trim ();
					composerView.Text = text;
					Post ();
				}
			}
		}
		
		void Post ()
		{
			var content = new StringBuilder ();
			var account = TwitterAccount.CurrentAccount;
			
			if (directRecipient == null){
				content.AppendFormat ("status={0}", OAuth.PercentEncode (composerView.Text));
				AppendLocation (content);
				if (InReplyTo != 0)
					content.AppendFormat ("&in_reply_to_status_id={0}", InReplyTo);	
				account.Post ("https://twitter.com/statuses/update.json", content.ToString ());
			} else {
				content.AppendFormat ("text={0}&user={1}", OAuth.PercentEncode (composerView.Text), OAuth.PercentEncode (directRecipient));
				AppendLocation (content);
				account.Post ("https://twitter.com/direct_messages/new.json", content.ToString ());
			}
			CloseComposer (this, EventArgs.Empty);
		}
		
		void KeyboardWillShow (object sender, UIKeyboardEventArgs e)
		{
			var kbdBounds = (e.Notification.UserInfo.ObjectForKey (UIKeyboard.BoundsUserInfoKey) as NSValue).CGRectValue;
			
			composerView.Frame = ComputeComposerSize (kbdBounds);
		}

		CGRect ComputeComposerSize (CGRect kbdBounds)
		{
			var view = View.Bounds;
			var nav = navigationBar.Bounds;

			return new CGRect (0, nav.Height, view.Width, view.Height-kbdBounds.Height-nav.Height);
		}
		
		public override void ViewWillAppear (bool animated)
		{
			base.ViewWillAppear (animated);
			composerView.textView.BecomeFirstResponder ();
		}
		
		void Activate (UIViewController parent)
		{
			previousController = parent;
			composerView.textView.BecomeFirstResponder ();
			parent.PresentModalViewController (this, true);
			
			if (Util.Defaults.IntForKey ("disableMusic") != 0)
				return;
			
			// Give some room to breathe on old systems
			NSTimer.CreateScheduledTimer (1, delegate {
				try {
					if (player == null)
						player = new AudioPlay ("Audio/composeaudio.mp3");
					player.Play ();
				} catch (Exception e){
					Console.WriteLine (e);
				}
			});
		}
		
		public void NewTweet (UIViewController parent)
		{
			NewTweet (parent, "");
		}
		
		public void NewTweet (UIViewController parent, string initialText)
		{
			ResetComposer (Locale.GetText ("New Tweet"), initialText);
			
			Activate (parent);
		}

		public void ReplyTo (UIViewController parent, Tweet source, bool replyAll)
		{
			ResetComposer (Locale.GetText ("Reply Tweet"), replyAll ? source.GetRecipients () : '@' + source.Screename + ' ');
			InReplyTo = source.Id;
			directRecipient = null;
			
			Activate (parent);
		}
		
		public void ReplyTo (UIViewController parent, Tweet source, string recipient)
		{
			ResetComposer (Locale.GetText ("Reply Tweet"), recipient	);
			InReplyTo = source.Id;
			directRecipient = null;
			
			Activate (parent);
		}

		public void Quote (UIViewController parent, Tweet source)
		{
			ResetComposer (Locale.GetText ("Quote"), "RT @" + source.Screename + " " + source.Text);
			
			Activate (parent);
		}
		
		public void Direct (UIViewController parent, string username)
		{
			ResetComposer (username == "" ? Locale.GetText ("Direct message") : Locale.Format ("Direct to {0}", username), "");
			directRecipient = username;
			
			Activate (parent);
		}
	}
	
	// Does anyone really use drafts? 
	public class Draft {
		static bool inited;
		
		static void Init ()
		{
			if (inited)
				return;
			inited = true;
			lock (Database.Main)
				Database.Main.CreateTable<Draft> ();
		}
		
		[PrimaryKey]
		public int Id { get; set; }
		public long AccountId { get; set; }
		public string Recipient { get; set; }
		public long InReplyTo { get; set; }
		public bool DirectMessage { get; set; }
		public string Message { get; set; }
	}
}
