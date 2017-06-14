﻿//
// A wrapper to play music in a loop
//
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
using System.IO;
using System.Threading;
using AVFoundation;
using AudioToolbox;
using Foundation;

namespace TweetStation
{
	//
	// Wrapper class to play audio in a loop, with a soft ending
	public class AudioPlay {
		AVAudioPlayer player;
		double lastTime;
		NSTimer timer = null;
		static bool inited;
		
		public static void InitAudio ()
		{
			if (inited)
				return;
			inited = true;
			AudioSession.Initialize ();
			AudioSession.Category = AudioSessionCategory.AmbientSound;
			
			try {
				AudioSession.OverrideCategoryMixWithOthers = true;
			} catch {
				// Not available in older OSes
			}
		}
		
		static AudioPlay ()
		{
			InitAudio ();
		}
		
		public AudioPlay (string file)
		{
			player = AVAudioPlayer.FromUrl (new NSUrl (file));
			if (player != null){
				player.NumberOfLoops = -1;
		
				player.Play ();
			}
		}

		public void Play ()
		{
			if (player == null)
				return;
			
			if (timer != null)
				timer.Invalidate ();
			player.Volume = 1;
			if (lastTime != 0)
				player.CurrentTime = lastTime;
			player.Play ();
		}
		
		// Slowly turn off the audio
		public void Stop ()
		{
			if (player == null)
				return;
			
			float volume = player.Volume;
			timer = NSTimer.CreateRepeatingScheduledTimer (TimeSpan.FromMilliseconds (100), delegate {
				volume -= 0.05f;
				player.Volume = volume;
				if (volume <= 0.1){
					lastTime = player.CurrentTime;
					player.Stop ();
					timer.Invalidate ();
				}
			});
		}
	}
}

