using Discord;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class AutoLegalityExtensionsDiscord
{
    public static async Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, ITrainerInfo sav, ShowdownSet set)
    {
        if (set.Species <= 0)
        {
            await channel.SendMessageAsync("Oops! I wasn't able to interpret your message! If you intended to convert something, please double check what you're pasting!").ConfigureAwait(false);
            return;
        }

        try
        {
            var template = AutoLegalityWrapper.GetTemplate(set);
            var pkm = sav.GetLegal(template, out var result);
            var la = new LegalityAnalysis(pkm);
            var spec = GameInfo.Strings.Species[template.Species];

            if (!la.Valid)
            {
                var reason = result switch
                {
                    "Timeout" => $"That {spec} set took too long to generate.",
                    "VersionMismatch" => "Request refused: PKHeX and Auto-Legality Mod version mismatch.",
                    _ => $"I wasn't able to create a {spec} from that set."
                };
                var imsg = $"Oops! {reason}";
                if (result == "Failed")
                    imsg += $"\n{AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm)}";
                await channel.SendMessageAsync(imsg).ConfigureAwait(false);
                return;
            }
            // Specie Img
            bool canGmax = pkm is PK8 pk8 && pk8.CanGigantamax;
            var speciesImage = TradeExtensions<PK9>.PokeImg(pkm, canGmax, false);

            // Create RegenTemplate from the legalized PKM
            var regenTemplate = new RegenTemplate(pkm);
            var regenText = regenTemplate.Text;

            // Get form name using FormConverter
            var formNames = FormConverter.GetFormList(pkm.Species, GameInfo.Strings.Types, GameInfo.Strings.forms, new List<string>(), pkm.Context);
            var formName = pkm.Form > 0 && pkm.Form < formNames.Length ? formNames[pkm.Form] : "";

            // Create species and form string
            var speciesForm = !string.IsNullOrEmpty(formName) ? $"{spec}-{formName}" : spec;
            var speciesInfo = $"{speciesForm}\n";

            // Prepend species information to the RegenTemplate
            regenText = speciesInfo + regenText;

            // Create embed
            var embed = new EmbedBuilder()
                .WithThumbnailUrl(speciesImage)
                .WithTitle($"Legalized RegenTemplate for {speciesForm}")
                .WithDescription($"Result: {result}\nEncounter: {la.EncounterOriginal.Name}")
                .AddField("RegenTemplate", $"```{regenText}```")
                .WithColor(Color.Green)
                .WithFooter("Copy the RegenTemplate text between the ``` marks to use it.");

            await channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(AutoLegalityExtensionsDiscord));
            var msg = $"Oops! An unexpected problem happened with this Showdown Set:\n```{string.Join("\n", set.GetSetLines())}```";
            await channel.SendMessageAsync(msg).ConfigureAwait(false);
        }
    }

    public static Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, string content, byte gen)
    {
        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var sav = AutoLegalityWrapper.GetTrainerInfo(gen);
        return channel.ReplyWithLegalizedSetAsync(sav, set);
    }

    public static Task ReplyWithLegalizedSetAsync<T>(this ISocketMessageChannel channel, string content) where T : PKM, new()
    {
        content = ReusableActions.StripCodeBlock(content);
        var set = new ShowdownSet(content);
        var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
        return channel.ReplyWithLegalizedSetAsync(sav, set);
    }

    public static async Task ReplyWithLegalizedSetAsync(this ISocketMessageChannel channel, IAttachment att)
    {
        var download = await NetUtil.DownloadPKMAsync(att).ConfigureAwait(false);
        if (!download.Success)
        {
            await channel.SendMessageAsync(download.ErrorMessage).ConfigureAwait(false);
            return;
        }

        var pkm = download.Data!;
        var embed = new EmbedBuilder();
        embed.Title = $"Legalization Report for {download.SanitizedFileName}";
        embed.Description = $"{download.SanitizedFileName} analysis and legalization attempt.";

        if (new LegalityAnalysis(pkm).Valid)
        {
            embed.Color = Color.Green;
            embed.AddField("Status", "Already legal.");
        }
        else
        {
            var legal = pkm.LegalizePokemon();
            if (!new LegalityAnalysis(legal).Valid)
            {
                embed.Color = Color.Red;
                embed.AddField("Status", "Unable to legalize.");
            }
            else
            {
                legal.RefreshChecksum();
                embed.Color = Color.Green;
                var msg = $"Here's your legalized PKM for {download.SanitizedFileName}!\n{ReusableActions.GetFormattedShowdownText(legal)}";
                embed.AddField("Status", "Successfully legalized.");
                embed.AddField("Details", msg);
                await channel.SendPKMAsync(legal).ConfigureAwait(false);
            }
        }

        await channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
    }
}
