using Discord;
using PKHeX.Core;
using SysBot.Pokemon.Helpers;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class EmbedHelper
{
    public static async Task SendNotificationEmbedAsync(IUser user, string message)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Notice")
            .WithDescription(message)
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/bdawg1989/sprites/main/exclamation.gif")
            .WithColor(Color.Red)
            .Build();

        await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    public static async Task SendTradeCanceledEmbedAsync(IUser user, string reason)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Your Trade was Canceled...")
            .WithDescription($"Your trade was canceled.\nPlease try again. If the issue persists, restart your switch and check your internet connection.\n\n**Reason**: {reason}")
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/bdawg1989/sprites/main/dmerror.gif")
            .WithColor(Color.Red)
            .Build();

        await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    public static async Task SendTradeCodeEmbedAsync(IUser user, int code)
    {
        var embed = new EmbedBuilder()
            .WithTitle("Here's your trade code!")
            .WithDescription($"# {code:0000 0000}")
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/bdawg1989/sprites/main/tradecode.gif")
            .WithColor(Color.Blue)
            .Build();

        await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    public static async Task SendTradeFinishedEmbedAsync<T>(IUser user, string message, T pk, bool isMysteryMon, bool isMysteryEgg)
        where T : PKM, new()
    {
        string thumbnailUrl;

        if (isMysteryEgg)
        {
            thumbnailUrl = "https://media.discordapp.net/attachments/1152944125818183681/1277245564085469246/IMG_5453.png?ex=66cc7720&is=66cb25a0&hm=10001509d9b4a63fb97589b5e424e401cce0b086a7812aed73b97aab90e57ff3&=&format=webp&quality=lossless&width=988&height=988";
        }
        else if (isMysteryMon)
        {
            thumbnailUrl = "https://media.discordapp.net/attachments/1152944125818183681/1277245563808776243/IMG_5454.webp?ex=66cc7720&is=66cb25a0&hm=8e787c5934990c6cef2518c4239c20810fa52bb75332b35245749c028b58cabd&=&format=webp&width=988&height=988";
        }
        else
        {
            thumbnailUrl = TradeExtensions<T>.PokeImg(pk, false, true, null);
        }

        var embed = new EmbedBuilder()
            .WithTitle("Trade Completed!")
            .WithDescription(message)
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl(thumbnailUrl)
            .WithColor(Color.Teal)
            .Build();

        await user.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    public static async Task SendTradeInitializingEmbedAsync(IUser user, string speciesName, int code, bool isMysteryMon, bool isMysteryEgg, string? message = null)
    {
        if (isMysteryEgg)
        {
            speciesName = "**Mystery Egg**";
        }
        else if (isMysteryMon)
        {
            speciesName = "**Mystery Pokemon**";
        }

        var embed = new EmbedBuilder()
            .WithTitle("Loading the Trade Portal...")
            .WithDescription($"**Pokemon**: {speciesName}\n**Trade Code**: {code:0000 0000}")
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/bdawg1989/sprites/main/initializing.gif")
            .WithColor(Color.Orange);

        if (!string.IsNullOrEmpty(message))
        {
            embed.WithDescription($"{embed.Description}\n\n{message}");
        }

        var builtEmbed = embed.Build();
        await user.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
    }

    public static async Task SendTradeSearchingEmbedAsync(IUser user, string trainerName, string inGameName, string? message = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"Now Searching for You, {trainerName}...")
            .WithDescription($"**Waiting for**: {trainerName}\n**My IGN**: {inGameName}")
            .WithTimestamp(DateTimeOffset.Now)
            .WithThumbnailUrl("https://raw.githubusercontent.com/bdawg1989/sprites/main/searching.gif")
            .WithColor(Color.Green);

        if (!string.IsNullOrEmpty(message))
        {
            embed.WithDescription($"{embed.Description}\n\n{message}");
        }

        var builtEmbed = embed.Build();
        await user.SendMessageAsync(embed: builtEmbed).ConfigureAwait(false);
    }
}
