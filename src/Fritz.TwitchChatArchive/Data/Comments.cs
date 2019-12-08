using System;
using System.Collections.Generic;
using System.Text;

namespace Fritz.TwitchChatArchive.Data
{


	public class CommentsRootData
	{
		public Comment[] comments { get; set; }
		public string _next { get; set; }
	}

	public class Comment
	{
		public string _id { get; set; }
		public DateTime created_at { get; set; }
		public DateTime updated_at { get; set; }
		public string channel_id { get; set; }
		public string content_type { get; set; }
		public string content_id { get; set; }
		public float content_offset_seconds { get; set; }
		public Commenter commenter { get; set; }
		public string source { get; set; }
		public string state { get; set; }
		public Message message { get; set; }
	}

	public class Commenter
	{
		public string display_name { get; set; }
		public string _id { get; set; }
		public string name { get; set; }
		public string type { get; set; }
		public string bio { get; set; }
		public DateTime created_at { get; set; }
		public DateTime updated_at { get; set; }
		public string logo { get; set; }
	}

	public class Message
	{
		public string body { get; set; }
		public Fragment[] fragments { get; set; }
		public bool is_action { get; set; }
		public User_Badges[] user_badges { get; set; }
		public string user_color { get; set; }
		public User_Notice_Params user_notice_params { get; set; }
		public int bits_spent { get; set; }
		public Emoticon1[] emoticons { get; set; }
	}

	public class User_Notice_Params
	{
		public string msgid { get; set; }
		public string msgparamcumulativemonths { get; set; }
		public string msgparammonths { get; set; }
		public string msgparamshouldsharestreak { get; set; }
		public string msgparamstreakmonths { get; set; }
		public string msgparamsubplan { get; set; }
		public string msgparamsubplanname { get; set; }
	}

	public class Fragment
	{
		public string text { get; set; }
		public Emoticon emoticon { get; set; }
	}

	public class Emoticon
	{
		public string emoticon_id { get; set; }
		public string emoticon_set_id { get; set; }
	}

	public class User_Badges
	{
		public string _id { get; set; }
		public string version { get; set; }
	}

	public class Emoticon1
	{
		public string _id { get; set; }
		public int begin { get; set; }
		public int end { get; set; }
	}


}
