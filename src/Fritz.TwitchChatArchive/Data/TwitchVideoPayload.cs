using System;
using System.Collections.Generic;
using System.Text;

namespace Fritz.TwitchChatArchive.Data
{
	public class TwitchVideo
	{
		public string title { get; set; }
		public object description { get; set; }
		public object description_html { get; set; }
		public long broadcast_id { get; set; }
		public string broadcast_type { get; set; }
		public string status { get; set; }
		public string tag_list { get; set; }
		public int views { get; set; }
		public string url { get; set; }
		public string language { get; set; }
		public DateTime created_at { get; set; }
		public string viewable { get; set; }
		public object viewable_at { get; set; }
		public DateTime published_at { get; set; }
		public DateTime delete_at { get; set; }
		public string _id { get; set; }
		public DateTime recorded_at { get; set; }
		public string game { get; set; }
		public int length { get; set; }
		public Preview preview { get; set; }
		public string animated_preview_url { get; set; }
		public Thumbnails thumbnails { get; set; }
		public Fps fps { get; set; }
		public string seek_previews_url { get; set; }
		public Resolutions resolutions { get; set; }
		public string restriction { get; set; }
		public Channel channel { get; set; }
		public string increment_view_count_url { get; set; }
	}

	public class Preview
	{
		public string small { get; set; }
		public string medium { get; set; }
		public string large { get; set; }
		public string template { get; set; }
	}

	public class Thumbnails
	{
		public Small[] small { get; set; }
		public Medium[] medium { get; set; }
		public Large[] large { get; set; }
		public Template[] template { get; set; }
	}

	public class Small
	{
		public string type { get; set; }
		public string url { get; set; }
	}

	public class Medium
	{
		public string type { get; set; }
		public string url { get; set; }
	}

	public class Large
	{
		public string type { get; set; }
		public string url { get; set; }
	}

	public class Template
	{
		public string type { get; set; }
		public string url { get; set; }
	}

	public class Fps
	{
		public float _160p30 { get; set; }
		public float _360p30 { get; set; }
		public float _480p30 { get; set; }
		public float _720p30 { get; set; }
		public float _720p60 { get; set; }
		public float chunked { get; set; }
	}

	public class Resolutions
	{
		public string _160p30 { get; set; }
		public string _360p30 { get; set; }
		public string _480p30 { get; set; }
		public string _720p30 { get; set; }
		public string _720p60 { get; set; }
		public string chunked { get; set; }
	}

	public class Channel
	{
		public bool mature { get; set; }
		public string status { get; set; }
		public string broadcaster_language { get; set; }
		public string broadcaster_software { get; set; }
		public string display_name { get; set; }
		public string game { get; set; }
		public string language { get; set; }
		public int _id { get; set; }
		public string name { get; set; }
		public DateTime created_at { get; set; }
		public DateTime updated_at { get; set; }
		public bool partner { get; set; }
		public string logo { get; set; }
		public string video_banner { get; set; }
		public string profile_banner { get; set; }
		public string profile_banner_background_color { get; set; }
		public string url { get; set; }
		public int views { get; set; }
		public int followers { get; set; }
		public string broadcaster_type { get; set; }
		public string description { get; set; }
		public bool private_video { get; set; }
		public bool privacy_options_enabled { get; set; }
	}


}
