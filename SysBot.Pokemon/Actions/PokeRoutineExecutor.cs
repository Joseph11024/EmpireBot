﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PKHeX.Core;
using SysBot.Base;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.PokeDataOffsets;

namespace SysBot.Pokemon
{
    public abstract class PokeRoutineExecutor : SwitchRoutineExecutor
    {
        protected PokeRoutineExecutor(string ip, int port) : base(ip, port) { }

        public async Task Click(SwitchButton b, int delayMin, int delayMax, CancellationToken token) =>
            await Click(b, Util.Rand.Next(delayMin, delayMax), token).ConfigureAwait(false);

        public async Task SetStick(SwitchStick stick, int x, int y, int delayMin, int delayMax, CancellationToken token) =>
            await SetStick(stick, x, y, Util.Rand.Next(delayMin, delayMax), token).ConfigureAwait(false);

        private static uint GetBoxSlotOffset(int box, int slot) => Box1Slot1 + (uint)(BoxFormatSlotSize * ((30 * box) + slot));

        public async Task<PK8> ReadPokemon(uint offset, CancellationToken token, int size = BoxFormatSlotSize)
        {
            var data = await Connection.ReadBytesAsync(offset, size, token).ConfigureAwait(false);
            return new PK8(data);
        }

        public async Task SetBoxPokemon(PK8 pkm, int box, int slot, CancellationToken token, SAV8? sav = null)
        {
            if (sav != null)
            {
                // Update PKM to the current save's handler data
                DateTime Date = DateTime.Now;
                pkm.Trade(sav, Date.Day, Date.Month, Date.Year);
                pkm.RefreshChecksum();
            }
            var ofs = GetBoxSlotOffset(box, slot);
            await Connection.WriteBytesAsync(pkm.EncryptedPartyData, ofs, token).ConfigureAwait(false);
        }

        public async Task<PK8> ReadBoxPokemon(int box, int slot, CancellationToken token)
        {
            var ofs = GetBoxSlotOffset(box, slot);
            return await ReadPokemon(ofs, token, BoxFormatSlotSize).ConfigureAwait(false);
        }

        public async Task<PK8?> ReadUntilPresent(uint offset, int waitms, int waitInterval, CancellationToken token, int size = BoxFormatSlotSize)
        {
            int msWaited = 0;
            while (msWaited < waitms)
            {
                var pk = await ReadPokemon(offset, token, size).ConfigureAwait(false);
                if (pk.Species != 0 && pk.ChecksumValid)
                    return pk;
                await Task.Delay(waitInterval, token).ConfigureAwait(false);
                msWaited += waitInterval;
            }
            return null;
        }

        public async Task<bool> ReadUntilChanged(uint offset, byte[] original, int waitms, int waitInterval, CancellationToken token)
        {
            int msWaited = 0;
            while (msWaited < waitms)
            {
                var result = await Connection.ReadBytesAsync(offset, original.Length, token).ConfigureAwait(false);
                if (!result.SequenceEqual(original))
                    return true;
                await Task.Delay(waitInterval, token).ConfigureAwait(false);
                msWaited += waitInterval;
            }
            return false;
        }

        public async Task<SAV8SWSH> IdentifyTrainer(CancellationToken token)
        {
            Connection.Log("Grabbing trainer data of host console...");
            var sav = await GetFakeTrainerSAV(token).ConfigureAwait(false);
            Connection.Name = $"{sav.OT}-{sav.DisplayTID}";
            Connection.Log($"{Connection.IP} identified as {Connection.Name}");
            return sav;
        }

        public static void DumpPokemon(string? folder, PKM pk)
        {
            if (folder == null)
                return;
            File.WriteAllBytes(Path.Combine(folder, Util.CleanFileName(pk.FileName)), pk.DecryptedPartyData);
        }

        public async Task<SAV8SWSH> GetFakeTrainerSAV(CancellationToken token)
        {
            var sav = new SAV8SWSH();
            var info = sav.MyStatus;
            var read = await Connection.ReadBytesAsync(TrainerDataOffset, TrainerDataLength, token).ConfigureAwait(false);
            read.CopyTo(info.Data);
            return sav;
        }

        protected async Task EnterTradeCode(int code, CancellationToken token)
        {
            for (int i = 0; i < 4; i++)
            {
                // Go to 0
                foreach (var e in arr[0])
                    await Click(e, 1000, token).ConfigureAwait(false);

                var digit = TradeUtil.GetCodeDigit(code, i);
                var entry = arr[digit];
                foreach (var e in entry)
                    await Click(e, 500, token).ConfigureAwait(false);

                // Confirm Digit
                await Click(A, 1_500, token).ConfigureAwait(false);
            }

            // Confirm Code outside of this method (allow synchronization)
        }

        public async Task<bool> IsGameConnected(CancellationToken token)
        {
            // Reads the Y-Com Flag is the Game is connected Online
            var data = await Connection.ReadBytesAsync(IsConnected, 1, token).ConfigureAwait(false);
            return data[0] == 1;
        }

        public async Task Reconnect_To_YCom(CancellationToken token)
        {
            // Press B in case a Error Message is Present
            await Click(B, 1000, token).ConfigureAwait(false);

            await Click(Y, 1000, token).ConfigureAwait(false);
            await Click(PLUS, 15_000, token).ConfigureAwait(false);

            for (int i = 0; i < 5; i++)
            {
                await Click(B, 500, token).ConfigureAwait(false);
            }
        }

        public async Task<bool> IsEggReady(Daycare daycare, CancellationToken token)
        {
            var ofs = daycare switch
            {
                Daycare.WildArea => DayCare_Wildarea_Egg_Is_Ready,
                Daycare.Route5 => DayCare_Route5_Egg_Is_Ready,
                _ => throw new ArgumentException(nameof(daycare)),
            };

            // Read a single byte of the Daycare metadata to check the IsEggReady flag.
            var data = await Connection.ReadBytesAsync(ofs, 1, token).ConfigureAwait(false);
            return data[0] == 1;
        }

        public async Task SetEggStepCounter(Daycare daycare, CancellationToken token)
        {
            var ofs = daycare switch
            {
                Daycare.WildArea => DayCare_Wildarea_Step_Counter,
                Daycare.Route5 => DayCare_Route5_Step_Counter,
                _ => throw new ArgumentException(nameof(daycare)),
            };

            // Set the step counter in the Daycare metadata to 180. This is the threshold that triggers the "Should I create a new egg" subroutine.
            // When the game executes the subroutine, it will generate a new seed and set the IsEggReady flag.
            // Just setting the IsEggReady flag won't refresh the seed; we want a different egg every time.
            var data = new byte[] { 0xB4, 0, 0, 0 }; // 180
            await Connection.WriteBytesAsync(data, ofs, token).ConfigureAwait(false);
        }

        public async Task<SlotQualityCheck> GetBoxSlotQuality(int box, int slot, CancellationToken token)
        {
            var b1s1 = await ReadBoxPokemon(box, slot, token).ConfigureAwait(false);
            return new SlotQualityCheck(b1s1);
        }

        public void PrintBadSlotMessage(SlotQualityCheck q)
        {
            switch (q.Quality)
            {
                case SlotQuality.BadData:
                    Connection.Log("Garbage detected in required Box Slot. Preventing execution.");
                    return;
                case SlotQuality.HasData:
                    Connection.Log("Required Box Slot not empty. Move this Pokemon before using the bot!");
                    Connection.Log(new ShowdownSet(q.Data!).Text);
                    return;
            }
        }

        public enum Daycare
        {
            WildArea,
            Route5
        }

        private static readonly SwitchButton[][] arr =
        {
            new[] {DDOWN, DDOWN, DDOWN }, // 0
            new[] {DUP, DUP, DUP, DLEFT}, // 1
            new[] {DUP, DUP, DUP,      }, // 2
            new[] {DUP, DUP, DUP,DRIGHT}, // 3
            new[] {DUP, DUP, DLEFT,    }, // 4
            new[] {DUP, DUP,           }, // 5
            new[] {DUP, DUP, DRIGHT,   }, // 6
            new[] {DUP, DLEFT,         }, // 7
            new[] {DUP,                }, // 8
            new[] {DUP, DRIGHT         }, // 9
        };
    }
}