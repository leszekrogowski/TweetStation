﻿﻿//
// Utilities for dealing with graphics
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
using System;
using System.Runtime.InteropServices;
using CoreGraphics;
using UIKit;
using ObjCRuntime;
using CoreAnimation;
using MonoTouch.Dialog;

namespace TweetStation
{
	public static class Graphics
	{
		static CGPath smallPath = GraphicsUtil.MakeRoundedPath (48, 4);
		static CGPath largePath = GraphicsUtil.MakeRoundedPath (73, 4);
		
		// Check for multi-tasking as a way to determine if we can probe for the "Scale" property,
		// only available on iOS4 
		public static bool HighRes = UIDevice.CurrentDevice.IsMultitaskingSupported && UIScreen.MainScreen.Scale > 1;
		
		static Selector sscale;

		[DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
		static extern void void_objc_msgSend_float(IntPtr deviceHandle, IntPtr setterHandle, float position);
		
		internal static void ConfigLayerHighRes (CALayer layer)
		{
			if (!HighRes)
				return;
			
			if (sscale == null)
				sscale = new Selector ("setContentsScale:");
			
			void_objc_msgSend_float (layer.Handle, sscale.Handle, 2.0f);
		}
		
		// Child proof the image by rounding the edges of the image
		internal static UIImage RemoveSharpEdges (UIImage image)
		{
			if (image == null)
				throw new ArgumentNullException ("image");
			
			float size = HighRes ? 73 : 48;
			
			UIGraphics.BeginImageContext (new CGSize (size, size));
			var c = UIGraphics.GetCurrentContext ();
			
			if (HighRes)
				c.AddPath (largePath);
			else 
				c.AddPath (smallPath);
			
			c.Clip ();
			
			image.Draw (new CGRect (0, 0, size, size));
			var converted = UIGraphics.GetImageFromCurrentImageContext ();
			UIGraphics.EndImageContext ();
			return converted;
		}
		
		//
		// Centers image, scales and removes borders
		//
		internal static UIImage PrepareForProfileView (UIImage image)
		{
			const int size = 73;
			if (image == null)
				throw new ArgumentNullException ("image");
			
			UIGraphics.BeginImageContext (new CGSize (73, 73));
			var c = UIGraphics.GetCurrentContext ();
			
			c.AddPath (largePath);
			c.Clip ();

			// Twitter not always returns squared images, adjust for that.
			var cg = image.CGImage;
			float width = cg.Width;
			float height = cg.Height;
			if (width != height){
				float x = 0, y = 0;
				if (width > height){
					x = (width-height)/2;
					width = height;
				} else {
					y = (height-width)/2;
					height = width;
				}
				c.ScaleCTM (1, -1);
				using (var copy = cg.WithImageInRect (new CGRect (x, y, width, height))){
					c.DrawImage (new CGRect (0, 0, size, -size), copy);
				}
			} else 
				image.Draw (new CGRect (0, 0, size, size));
			
			var converted = UIGraphics.GetImageFromCurrentImageContext ();
			UIGraphics.EndImageContext ();
			return converted;
		}
	}
	
	public class TriangleView : UIView {
		UIColor fill, stroke;
		
		public TriangleView (UIColor fill, UIColor stroke) 
		{
			Opaque = false;
			this.fill = fill;
			this.stroke = stroke;
		}
		
		public override void Draw (CGRect rect)
		{
			var context = UIGraphics.GetCurrentContext ();
			var b = Bounds;
			
			fill.SetColor ();
			context.MoveTo (0, b.Height);
			context.AddLineToPoint (b.Width/2, 0);
			context.AddLineToPoint (b.Width, b.Height);
			context.ClosePath ();
			context.FillPath ();
			
			stroke.SetColor ();
			context.MoveTo (0, b.Width/2);
			context.AddLineToPoint (b.Width/2, 0);
			context.AddLineToPoint (b.Width, b.Width/2);
			context.StrokePath ();
		}
	}
	
}
