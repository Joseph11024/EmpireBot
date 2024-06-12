using Discord;
using Discord.Commands;
using Discord.WebSocket;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static SysBot.Pokemon.DiscordSettings;

namespace SysBot.Pokemon.Discord
{
    public static class EmbedColorConverter
    {
        public static Color ToDiscordColor(this EmbedColorOption colorOption)
        {
            return colorOption switch
            {
                EmbedColorOption.Blue => Color.Blue,
                EmbedColorOption.Green => Color.Green,
                EmbedColorOption.Red => Color.Red,
                EmbedColorOption.Gold => Color.Gold,
                EmbedColorOption.Purple => Color.Purple,
                EmbedColorOption.Teal => Color.Teal,
                EmbedColorOption.Orange => Color.Orange,
                EmbedColorOption.Magenta => Color.Magenta,
                EmbedColorOption.LightGrey => Color.LightGrey,
                EmbedColorOption.DarkGrey => Color.DarkGrey,
                _ => Color.Blue,  // Default to Blue if somehow an undefined enum value is used
            };
        }
    }

    public class EchoModule : ModuleBase<SocketCommandContext>
    {
        private static DiscordSettings? Settings { get; set; }

        private class EchoChannel(ulong channelId, string channelName, Action<string> action, Action<byte[], string, EmbedBuilder> raidAction)
        {
            public readonly ulong ChannelID = channelId;

            public readonly string ChannelName = channelName;

            public readonly Action<string> Action = action;

            public readonly Action<byte[], string, EmbedBuilder> RaidAction = raidAction;

            public string EmbedResult = string.Empty;
        }

        private class EncounterEchoChannel(ulong channelId, string channelName, Action<string, Embed> embedaction)
        {
            public readonly ulong ChannelID = channelId;

            public readonly string ChannelName = channelName;

            public readonly Action<string, Embed> EmbedAction = embedaction;

            public string EmbedResult = string.Empty;
        }

        private static readonly Dictionary<ulong, EchoChannel> Channels = [];

        private static readonly Dictionary<ulong, EncounterEchoChannel> EncounterChannels = [];

        public static void RestoreChannels(DiscordSocketClient discord, DiscordSettings cfg)
        {
            Settings = cfg;
            foreach (var ch in cfg.AnnouncementChannels)
            {
                if (discord.GetChannel(ch.ID) is ISocketMessageChannel c)
                    AddEchoChannel(c, ch.ID);
            }

            // EchoUtil.Echo("Added echo notification to Discord channel(s) on Bot startup.");
        }

        [Command("Announce", RunMode = RunMode.Async)]
        [Alias("announce")]
        [Summary("Sends an announcement to all EchoChannels added by the aec command.")]
        [RequireSudo]
        public async Task AnnounceAsync([Remainder] string announcement)
        {
            var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var formattedTimestamp = $"<t:{unixTimestamp}:F>";
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            var embedColor = Settings.AnnouncementSettings.RandomAnnouncementColor ? GetRandomColor() : Settings.AnnouncementSettings.AnnouncementEmbedColor.ToDiscordColor();
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            var thumbnailUrl = Settings.AnnouncementSettings.RandomAnnouncementThumbnail ? GetRandomThumbnail() : GetSelectedThumbnail();

            var embedDescription = $"## {announcement}\n\n**Sent: {formattedTimestamp}**";

            var embed = new EmbedBuilder
            {
                Color = embedColor,
                Description = embedDescription
            }
            .WithTitle("Important Announcement from the Empire!")
            .WithThumbnailUrl(thumbnailUrl)
            .Build();

            var client = Context.Client;
            foreach (var channelEntry in Channels)
            {
                var channelId = channelEntry.Key;
                if (client.GetChannel(channelId) is not ISocketMessageChannel channel)
                {
                    LogUtil.LogError($"Failed to find or access channel {channelId}", nameof(AnnounceAsync));
                    continue;
                }

                try
                {
                    await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Failed to send announcement to channel {channel.Name}: {ex.Message}", nameof(AnnounceAsync));
                }
            }
            var confirmationMessage = await ReplyAsync("Announcement sent to all EchoChannels.").ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await confirmationMessage.DeleteAsync().ConfigureAwait(false);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
        }

        private static Color GetRandomColor()
        {
            var random = new Random();
            var colors = Enum.GetValues(typeof(EmbedColorOption)).Cast<EmbedColorOption>().ToList();
            return colors[random.Next(colors.Count)].ToDiscordColor();
        }

        private static string GetRandomThumbnail()
        {
            var thumbnailOptions = new List<string>
    {
        "https://media.discordapp.net/attachments/1152944125818183681/1218757568432836628/Announcement_1.png?ex=6608d36f&is=65f65e6f&hm=efe8af3813bb18e323617e18c6f305d7d69e74bfe8d3fc53d049d18604a82776&=&format=webp&quality=lossless&width=970&height=988",
        "https://media.discordapp.net/attachments/1152944125818183681/1218757568839548928/Announcement_2.png?ex=6608d36f&is=65f65e6f&hm=91bcd6972fb2e59ba90cbe6d28f4c44b107da81af6995933005b87bca48e27dc&=&format=webp&quality=lossless&width=970&height=988",
        "https://media.discordapp.net/attachments/1152944125818183681/1218757569175228507/Announcement_3.png?ex=6608d36f&is=65f65e6f&hm=9d1a7dc5706bf6ebd24eedf85a532f51746e6ace93d00a32af7a0bc5d8129cc0&=&format=webp&quality=lossless&width=1002&height=988",
        "https://media.discordapp.net/attachments/1152944125818183681/1218757569510637718/Announcement_4.png?ex=6608d36f&is=65f65e6f&hm=6167fc4e2c586cc8ad277cb498723eebca2a548985eed1b11f6b177ce1f821eb&=&format=webp&quality=lossless&width=1002&height=988",
        "https://media.discordapp.net/attachments/1152944125818183681/1218757569808695327/Announcement_5.png?ex=6608d370&is=65f65e70&hm=edd5d102e8e5a12c4a54fdceb965684b50f92b5940e9a06b2e8b5611b500366a&=&format=webp&quality=lossless&width=1002&height=988",
    };
            var random = new Random();
            return thumbnailOptions[random.Next(thumbnailOptions.Count)];
        }

        private static string GetSelectedThumbnail()
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (!string.IsNullOrEmpty(Settings.AnnouncementSettings.CustomAnnouncementThumbnailUrl))
            {
                return Settings.AnnouncementSettings.CustomAnnouncementThumbnailUrl;
            }
            else
            {
                return GetUrlFromThumbnailOption(Settings.AnnouncementSettings.AnnouncementThumbnailOption);
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }

        private static string GetUrlFromThumbnailOption(ThumbnailOption option)
        {
            return option switch
            {
                ThumbnailOption.Gengar => "https://media.discordapp.net/attachments/1152944125818183681/1218757568432836628/Announcement_1.png?ex=6608d36f&is=65f65e6f&hm=efe8af3813bb18e323617e18c6f305d7d69e74bfe8d3fc53d049d18604a82776&=&format=webp&quality=lossless&width=970&height=988",
                ThumbnailOption.Pikachu => "https://media.discordapp.net/attachments/1152944125818183681/1218757568839548928/Announcement_2.png?ex=6608d36f&is=65f65e6f&hm=91bcd6972fb2e59ba90cbe6d28f4c44b107da81af6995933005b87bca48e27dc&=&format=webp&quality=lossless&width=970&height=988",
                ThumbnailOption.Umbreon => "https://media.discordapp.net/attachments/1152944125818183681/1218757569175228507/Announcement_3.png?ex=6608d36f&is=65f65e6f&hm=9d1a7dc5706bf6ebd24eedf85a532f51746e6ace93d00a32af7a0bc5d8129cc0&=&format=webp&quality=lossless&width=1002&height=988",
                ThumbnailOption.Sylveon => "https://media.discordapp.net/attachments/1152944125818183681/1218757569510637718/Announcement_4.png?ex=6608d36f&is=65f65e6f&hm=6167fc4e2c586cc8ad277cb498723eebca2a548985eed1b11f6b177ce1f821eb&=&format=webp&quality=lossless&width=1002&height=988",
                ThumbnailOption.Charmander => "https://media.discordapp.net/attachments/1152944125818183681/1218757569808695327/Announcement_5.png?ex=6608d370&is=65f65e70&hm=edd5d102e8e5a12c4a54fdceb965684b50f92b5940e9a06b2e8b5611b500366a&=&format=webp&quality=lossless&width=1002&height=988",
                ThumbnailOption.Jigglypuff => "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/jigglypuffmegaphone.png",
                ThumbnailOption.Flareon => "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/flareonmegaphone.png",
                _ => "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/gengarmegaphone.png",
            };
        }

        [Command("addEmbedChannel")]
        [Alias("aec")]
        [Summary("Makes the bot post raid embeds to the channel.")]
        [RequireSudo]
        public async Task AddEchoAsync()
        {
            var c = Context.Channel;
            var cid = c.Id;
            if (Channels.TryGetValue(cid, out _))
            {
                await ReplyAsync("Already notifying here.").ConfigureAwait(false);
                return;
            }

            AddEchoChannel(c, cid);

            SysCordSettings.Settings.AnnouncementChannels.AddIfNew([GetReference(Context.Channel)]);
            await ReplyAsync("Added Trade Embed output to this channel!").ConfigureAwait(false);
        }

        private static async Task<bool> SendMessageWithRetry(ISocketMessageChannel c, string message, int maxRetries = 3)
        {
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                try
                {
                    await c.SendMessageAsync(message).ConfigureAwait(false);
                    return true; // Successfully sent the message, exit the loop.
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Failed to send message to channel '{c.Name}' (Attempt {retryCount + 1}): {ex.Message}", nameof(AddEchoChannel));
                    retryCount++;
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false); // Wait for 5 seconds before retrying.
                }
            }
            return false; // Reached max number of retries without success.
        }

        private static async Task<bool> RaidEmbedAsync(ISocketMessageChannel c, byte[] bytes, string fileName, EmbedBuilder embed, int maxRetries = 2)
        {
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                try
                {
                    if (bytes is not null && bytes.Length > 0)
                    {
                        await c.SendFileAsync(new MemoryStream(bytes), fileName, "", false, embed: embed.Build()).ConfigureAwait(false);
                    }
                    else
                    {
                        await c.SendMessageAsync("", false, embed.Build()).ConfigureAwait(false);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Failed to send embed to channel '{c.Name}' (Attempt {retryCount + 1}): {ex.Message}", nameof(AddEchoChannel));
                    retryCount++;
                    if (retryCount < maxRetries)
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false); // Wait for a second before retrying.
                }
            }
            return false;
        }

        private static void AddEchoChannel(ISocketMessageChannel c, ulong cid)
        {
            async void l(string msg) => await SendMessageWithRetry(c, msg).ConfigureAwait(false);
            async void rb(byte[] bytes, string fileName, EmbedBuilder embed) => await RaidEmbedAsync(c, bytes, fileName, embed).ConfigureAwait(false);

            EchoUtil.Forwarders.Add(l);
            var entry = new EchoChannel(cid, c.Name, l, rb);
            Channels.Add(cid, entry);
        }

        public static bool IsEchoChannel(ISocketMessageChannel c)
        {
            var cid = c.Id;
            return Channels.TryGetValue(cid, out _);
        }

        public static bool IsEmbedEchoChannel(ISocketMessageChannel c)
        {
            var cid = c.Id;
            return EncounterChannels.TryGetValue(cid, out _);
        }

        [Command("echoInfo")]
        [Summary("Dumps the special message (Echo) settings.")]
        [RequireSudo]
        public async Task DumpEchoInfoAsync()
        {
            foreach (var c in Channels)
                await ReplyAsync($"{c.Key} - {c.Value}").ConfigureAwait(false);
        }

        [Command("echoClear")]
        [Alias("rec")]
        [Summary("Clears the special message echo settings in that specific channel.")]
        [RequireSudo]
        public async Task ClearEchosAsync()
        {
            var id = Context.Channel.Id;
            if (!Channels.TryGetValue(id, out var echo))
            {
                await ReplyAsync("Not echoing in this channel.").ConfigureAwait(false);
                return;
            }
            EchoUtil.Forwarders.Remove(echo.Action);
            Channels.Remove(Context.Channel.Id);
            SysCordSettings.Settings.AnnouncementChannels.RemoveAll(z => z.ID == id);
            await ReplyAsync($"Echoes cleared from channel: {Context.Channel.Name}").ConfigureAwait(false);
        }

        [Command("echoClearAll")]
        [Alias("raec")]
        [Summary("Clears all the special message Echo channel settings.")]
        [RequireSudo]
        public async Task ClearEchosAllAsync()
        {
            foreach (var l in Channels)
            {
                var entry = l.Value;
                await ReplyAsync($"Echoing cleared from {entry.ChannelName} ({entry.ChannelID}!").ConfigureAwait(false);
                EchoUtil.Forwarders.Remove(entry.Action);
            }
            EchoUtil.Forwarders.RemoveAll(y => Channels.Select(x => x.Value.Action).Contains(y));
            Channels.Clear();
            SysCordSettings.Settings.AnnouncementChannels.Clear();
            await ReplyAsync("Echoes cleared from all channels!").ConfigureAwait(false);
        }

        private RemoteControlAccess GetReference(IChannel channel) => new()
        {
            ID = channel.Id,
            Name = channel.Name,
            Comment = $"Added by {Context.User.Username} on {DateTime.Now:yyyy.MM.dd-hh:mm:ss}",
        };
    }
}
