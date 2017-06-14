using System;
using Foundation;
using UIKit;
using MonoTouch.Dialog;
using CoreGraphics;

namespace TweetStation
{
	public class Hud : UIView {
		protected CGRect HudRect;
		
		public Hud () : base (UIScreen.MainScreen.Bounds)
		{
			BackgroundColor = UIColor.Clear;
		}
		
		public override void Draw (CGRect rect)
		{
			this.DrawRoundRectangle (HudRect, 8, UIColor.FromRGBA (0, 0, 0, 190));
		}
	}
	
	public class ProgressHud : Hud {
		const int minWidth = 240;
		CGSize captionSize;
		UIFont font;
		string caption, buttonText;
		UIButton button;
		float progress;
		CGRect progressRect;
		
		public event Action ButtonPressed;
		public float Progress {
			get {
				return progress;
			}
			set {
				progress = value;
				SetNeedsDisplay ();
			}
		}
		
		public ProgressHud (string caption, string buttonText)
		{
			font = UIFont.BoldSystemFontOfSize (16);
			
			this.caption = caption;
			this.buttonText = buttonText;
			button = UIButton.FromType (UIButtonType.Custom);
			button.BackgroundColor = UIColor.Clear;
			button.Font = UIFont.BoldSystemFontOfSize (16);
			button.SetTitle (buttonText, UIControlState.Normal);
			button.SetTitleColor (UIColor.White, UIControlState.Normal);
			button.TouchDown += delegate { 
				if (ButtonPressed == null)
					return;
				ButtonPressed ();
			};
			var layer = button.Layer;
			layer.CornerRadius = 4;
			layer.BorderWidth = 2;
			layer.BorderColor = UIColor.White.CGColor;
			
			AddSubview (button);
		}

		public override void LayoutSubviews ()
		{
			base.LayoutSubviews ();

			captionSize = UIStringDrawing.StringSize (caption, font);
			nfloat width = captionSize.Width < minWidth ? minWidth : captionSize.Width;
			var bounds = Bounds;
			
			HudRect = new CGRect ((bounds.Width-width)/2, bounds.Height > bounds.Width ? 60 : 30, width, 120);
			
			var ss = UIStringDrawing.StringSize (buttonText, font);
			var sh = Math.Max (ss.Height, 30);
			var sw = Math.Max (ss.Width, 60);
			button.Frame = new CGRect (HudRect.Right-sw-20, HudRect.Bottom-sh-10, sw+5, sh);
			
			progressRect = new CGRect (HudRect.Left+10, HudRect.Y+45, HudRect.Width-20, 10);
		}
		
		public override void Draw (CGRect rect)
		{
			base.Draw (rect);
			UIColor.White.SetColor ();
			caption.DrawString (new CGPoint (HudRect.X + (HudRect.Width-captionSize.Width)/2, HudRect.Y + 10), font);

			var ctx = UIGraphics.GetCurrentContext ();
			using (var path = GraphicsUtil.MakeRoundedRectPath (progressRect, 5)){
				ctx.SetLineWidth (2);
				ctx.AddPath (path);
				ctx.StrokePath ();
			}
			var prect = progressRect.Inset (3, 3);
			prect.Width *= Progress;
			using (var path = GraphicsUtil.MakeRoundedRectPath (prect, 2)){
				ctx.AddPath (path);
				ctx.FillPath ();
			}
//			UIColor.White.SetColor ();
		//	ctx.AddRect (progressRect);
		//	ctx.StrokePath ();
		}
	}
}

