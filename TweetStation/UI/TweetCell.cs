//
// TweetCell.cs: 
//
// This shows both how to implement a custom UITableViewCell and
// how to implement a custom MonoTouch.Dialog.Element.
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
using System.Collections;
using UIKit;
using Foundation;
using MonoTouch.Dialog;
using System.Drawing;

namespace TweetStation
{
	// 
	// TweetCell used for the timelines.   It is relatlively light, and 
	// does not do highlighting.   This might work for the iPhone, but
	// for the iPad we probably should just use TweetViews that do the
	// highlighting of url-like things
	//
	public class TweetCell : UITableViewCell {
		const int userSize = 14;
		const int textSize = 15;
		const int timeSize = 12;
		
		const int PicSize = 48;
		const int PicXPad = 10;
		const int PicYPad = 5;
		
		const int PicAreaWidth = 2 * PicXPad + PicSize;
		
		const int TextHeightPadding = 4;
		const int TextWidthPadding = 4;
		const int TextYOffset = userSize + 8;
		const int MinHeight = PicSize + 2 * PicYPad;
		const int TimeWidth = 46;
		
		static UIFont userFont = UIFont.BoldSystemFontOfSize (userSize);
		static UIFont textFont = UIFont.SystemFontOfSize (textSize);
		static UIFont timeFont = UIFont.SystemFontOfSize (timeSize);
		static UIColor timeColor = UIColor.FromRGB (147, 170, 204);
		
		TweetCellView tweetView;
		
		static CGGradient bottomGradient, topGradient;
		static CGPath badgePath = GraphicsUtil.MakeRoundedPath (48, 4);
		static CGPath smallBadgePath = GraphicsUtil.MakeRoundedPath (23, 4);
		
		public static int CellStyle;
		
		static TweetCell ()
		{
			using (var rgb = CGColorSpace.CreateDeviceRGB()){
				nfloat [] colorsBottom = {
					1, 1, 1, .5f,
					0.93f, 0.93f, 0.93f, .5f
				};
				bottomGradient = new CGGradient (rgb, colorsBottom, null);
				nfloat [] colorsTop = {
					0.93f, 0.93f, 0.93f, .5f,
					1, 1, 1, 0.5f
				};
				topGradient = new CGGradient (rgb, colorsTop, null);
			}
		}
		
		// Should never happen
		public TweetCell (IntPtr handle) : base (handle) {
			Console.WriteLine (Environment.StackTrace);
		}
		
		public class TweetCellView : UIView, IImageUpdated {
			static UIImage star = UIImage.FromBundle ("Images/mini-star-on.png");	
			Tweet tweet;
			string userText;
			UIImage tweetImage, retweetImage;
			
			public TweetCellView (Tweet tweet) : base ()
			{
				Update (tweet);
				Opaque = true;
				BackgroundColor = UIColor.White;
			}

			public void Update (Tweet tweet)
			{
				this.tweet = tweet;
				userText = tweet.Retweeter == null ? tweet.Screename : tweet.Screename + "→" + tweet.Retweeter;
	
				// 
				// For fake UserIDs (returned by the search), we try looking up by screename now
				//
				var img = ImageStore.GetLocalProfilePicture (tweet.UserId);
				if (img == null)
					img = ImageStore.GetLocalProfilePicture (tweet.Screename);
				if (img == null)
					ImageStore.QueueRequestForPicture (tweet.UserId, tweet.PicUrl, this);
				else 
					tweet.PicUrl = null;
				tweetImage = img == null ? ImageStore.DefaultImage : img;
				if (tweet.Retweeter == null)
					retweetImage = null;
				else {
					img = ImageStore.GetLocalProfilePicture (tweet.RetweeterId);
					if (img == null)
						ImageStore.QueueRequestForPicture (tweet.RetweeterId, tweet.RetweeterPicUrl, this);
					else 
						tweet.RetweeterPicUrl = null;
					
					retweetImage = img == null ? ImageStore.DefaultImage : img;
				}

				SetNeedsDisplay ();
			}
			
			public override void Draw (CGRect rect)
			{
				var start = DateTime.UtcNow.Ticks;
				var context = UIGraphics.GetCurrentContext ();

				// Superview is the container, its superview the uitableviewcell
				bool highlighted = (Superview.Superview as UITableViewCell).Highlighted;
				UIColor textColor;
	
				var bounds = Bounds;
				var midx = bounds.Width/2;
				if (highlighted){
					UIColor.FromRGB (4, 0x79, 0xef).SetColor ();
					context.FillRect (bounds);
					Images.MenuShadow.Draw (bounds, CGBlendMode.Normal, 0.5f);
					textColor = UIColor.White;
				} else {
					UIColor.White.SetColor ();
					context.FillRect (bounds);
					context.DrawLinearGradient (bottomGradient, new CGPoint (midx, bounds.Height-17), new CGPoint (midx, bounds.Height), 0);
					context.DrawLinearGradient (topGradient, new CGPoint (midx, 1), new CGPoint (midx, 3), 0);
					textColor = UIColor.Black;
				}
				
				nfloat xPic, xText;
				
				if ((CellStyle & 1) == 0 && tweet.UserId == TwitterAccount.CurrentAccount.AccountId){
					xText = TextWidthPadding;
					xPic = bounds.Width-PicAreaWidth+PicXPad;
				} else {
					xText = PicAreaWidth;
					xPic = PicXPad;
				}
				if (userText == null){
					userText = "Unknown";
				}
				textColor.SetColor ();
				userText.DrawString (new CGRect (xText, TextHeightPadding, bounds.Width-PicAreaWidth-TextWidthPadding-TimeWidth, userSize), userFont);
				tweet.Text.DrawString (new CGRect (xText, bounds.Y + TextYOffset, bounds.Width-PicAreaWidth-TextWidthPadding, bounds.Height-TextYOffset), textFont, UILineBreakMode.WordWrap);
				timeColor.SetColor ();
				string time = Util.FormatTime (new TimeSpan (DateTime.UtcNow.Ticks - tweet.CreatedAt));
				if (tweet.Favorited){
					using (var nss = new NSString (time)){
						var size = nss.StringSize (timeFont);
						
						star.Draw (new CGRect (bounds.Width-24-size.Width-(xPic == PicXPad ? 0 : PicAreaWidth), TextHeightPadding, size.Height, size.Height));
					}
				}
				time.DrawString (new CGRect (xText, TextHeightPadding, bounds.Width-PicAreaWidth-TextWidthPadding, timeSize),
				            timeFont, UILineBreakMode.Clip, UITextAlignment.Right);
				
				if ((CellStyle & 2) == 0){
					// Cute touch
					UIColor.White.SetColor ();
					context.SaveState ();
					context.TranslateCTM (xPic, PicYPad);
					context.SetLineWidth (1);
					
					// On device, the shadow is painted in the opposite direction!
					context.SetShadow (new CGSize (1, 1), 3, UIColor.DarkGray.CGColor);
					context.AddPath (badgePath);
					context.FillPath ();
					if (retweetImage != null){
						context.TranslateCTM (30, 30);
						context.AddPath (smallBadgePath);
						context.StrokePath ();
					}
					context.RestoreState ();
				}
				tweetImage.Draw (new CGRect (xPic, PicYPad, PicSize, PicSize));
				
				if (retweetImage != null)
					retweetImage.Draw (new CGRect (xPic+30, PicYPad+30, 23, 23));
				
				ticks += DateTime.UtcNow.Ticks - start;
			}
			long ticks;
			
			void IImageUpdated.UpdatedImage (long onId)
			{
				// Discard notifications that might have been queued for an old cell
				if (tweet == null || (tweet.UserId != onId && tweet.RetweeterId != onId))
					return;
				
				// Discard the url string once the image is loaded, we wont be using it.
				if (onId == tweet.UserId){
					tweetImage = ImageStore.GetLocalProfilePicture (onId);
					tweet.PicUrl = null;
				} else {
					retweetImage = ImageStore.GetLocalProfilePicture (onId);
					tweet.RetweeterPicUrl = null;
				}
				SetNeedsDisplay ();
			}
		}
		
		// Create the UIViews that we will use here, layout happens in LayoutSubviews
		public TweetCell (UITableViewCellStyle style, NSString ident, Tweet tweet) : base (style, ident)
		{
			SelectionStyle = UITableViewCellSelectionStyle.Blue;
			
			tweetView = new TweetCellView (tweet);
			UpdateCell (tweet);
			ContentView.Add (tweetView);
		}

		// 
		// This method is called when the cell is reused to reset
		// all of the cell values
		//
		public void UpdateCell (Tweet tweet)
		{
			tweetView.Update (tweet);
			SetNeedsDisplay ();
		}

		public static float GetCellHeight (CGRect bounds, Tweet tweet)
		{
			bounds.Height = 999;
			
			// Keep the same as LayoutSubviews
			bounds.X = 0;
			bounds.Width -= PicAreaWidth+TextWidthPadding;
			
			using (var nss = new NSString (tweet.Text)){
				var dim = nss.StringSize (textFont, bounds.Size, UILineBreakMode.WordWrap);
				return (float)Math.Max (dim.Height + TextYOffset + 2*TextWidthPadding, MinHeight);
			}
		}

		public override void LayoutSubviews ()
		{
			base.LayoutSubviews ();
			
			tweetView.Frame = ContentView.Bounds;
			tweetView.SetNeedsDisplay ();
		}
	}
	
	// 
	// A MonoTouch.Dialog.Element that renders a TweetCell
	//
	public class TweetElement : Element, IElementSizing {
		static NSString key = new NSString ("tweetelement");
		public Tweet Tweet;

		public TweetElement (Tweet tweet) : base (null)
		{
			Tweet = tweet;	
		}
		
		// Gets a cell on demand, reusing cells
		public override UITableViewCell GetCell (UITableView tv)
		{
			var cell = tv.DequeueReusableCell (key) as TweetCell;
			if (cell == null)
				cell = new TweetCell (UITableViewCellStyle.Default, key, Tweet);
			else
				cell.UpdateCell (Tweet);
			
			return cell;
		}
		
		public override void Selected (DialogViewController dvc, UITableView tableView, NSIndexPath path)
		{
			var profile = new DetailTweetViewController (Tweet);
			dvc.ActivateController (profile);
		}

		public override bool Matches (string text)
		{
			return (Tweet.Screename.IndexOf (text, StringComparison.CurrentCultureIgnoreCase) != -1) || 
				(Tweet.Text.IndexOf (text, StringComparison.InvariantCultureIgnoreCase) != -1) || 
				(Tweet.Retweeter != null ? Tweet.Retweeter.IndexOf (text, StringComparison.CurrentCultureIgnoreCase) != -1 : false);
		}
		
		#region IElementSizing implementation
		public nfloat GetHeight (UITableView tableView, NSIndexPath indexPath)
		{
			return TweetCell.GetCellHeight (tableView.Bounds, Tweet);
		}
		#endregion
	}
}
