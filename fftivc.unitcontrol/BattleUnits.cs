namespace fftivc.unitcontrol
{
    /// <summary>
    /// Layout of the game's battle unit array and the fields the mod reads and writes.
    /// </summary>
    /// <remarks>
    /// Recovered from FFT_enhanced 1.5.2 and cross-checked against 1.2.0.
    /// <para>
    /// The array is a fixed global (named <c>BattleUnits__bwork</c> at <c>0x141853CE0</c> in
    /// 1.5.2) holding <see cref="Capacity"/> contiguous <see cref="Stride"/>-byte records.
    /// The game itself walks it with exactly these bounds, e.g. <c>check_game_cont</c>
    /// iterates up to <c>0x1418566E0</c> (the first byte past the array:
    /// <c>0x1418566E0 - 0x141853CE0 = 0x2A00 = 21 * 0x200</c>) and <c>sub_140280ED0</c>
    /// loops <c>while (i &lt; 21)</c>.
    /// </para>
    /// <para>
    /// Field names below follow the IDB's <c>BattleUnit</c> struct
    /// (<see cref="Stride"/> bytes, 227 members).
    /// </para>
    /// </remarks>
    internal static class BattleUnits
    {
        /// <summary>Number of unit slots in the array. <c>0x1418566E0 - 0x141853CE0 = 0x2A00 = 21 * 0x200</c>.</summary>
        public const int Capacity = 21;

        /// <summary>Size of one <c>BattleUnit</c> record, in bytes. Matches <c>sizeof(BattleUnit)</c>.</summary>
        public const int Stride = 0x200;

        /* ---- BattleUnit field offsets ---- */

        /// <summary><c>BattleUnit.SpriteSet</c> (enum, 1 byte).</summary>
        public const int OffsetSpriteSet = 0x00;

        /// <summary><c>BattleUnit.Index</c>. <see cref="EmptyIndex"/> marks an unused slot.</summary>
        public const int OffsetIndex = 0x01;

        /// <summary><c>BattleUnit.Job</c> (JobID, 1 byte).</summary>
        public const int OffsetJob = 0x03;

        /// <summary><c>BattleUnit.Flags2</c> (EventUnit_Flags2, 1 byte).</summary>
        public const int OffsetFlags2 = 0x05;

        /// <summary><c>BattleUnit.Flags</c> (EventUnit_Flags1, 1 byte).</summary>
        public const int OffsetFlags1 = 0x06;

        /// <summary>
        /// <c>BattleUnit.Flags2_2</c> (EventUnit_Flags2, 1 byte) - a second copy of
        /// <see cref="OffsetFlags2"/>. Game code reads this copy far more often; the two are
        /// XORed together for comparisons in <c>sub_140280ED0</c>.
        /// </summary>
        public const int OffsetFlags2Mirror = 0x1EE;

        /* ---- Flag bit meanings ---- */

        /// <summary><c>BattleUnit.Index</c> value marking an unused slot.</summary>
        public const byte EmptyIndex = 0xFF;

        /// <summary>
        /// Team bits inside <c>Flags2</c> / <c>Flags2_2</c>. Non-zero means the unit is not on
        /// the player's team.
        /// <para>
        /// CONFIRMED: <c>check_game_cont</c> partitions every live unit into two counters with
        /// <c>(*Flags2_2 &amp; 0x30) != 0</c>, and <c>sub_140280ED0</c> and <c>CheckBowResult</c>
        /// compare units with <c>(a ^ b) &amp; 0x30</c>.
        /// </para>
        /// </summary>
        public const byte Flags2EnemyMask = 0x30;

        /// <summary>
        /// Human/player control bit inside <c>Flags2</c> / <c>Flags2_2</c>. This is the bit the
        /// mod sets to hand a unit to the player and clears to give it back to the AI.
        /// <para>
        /// CONFIRMED: <c>unitCommandInitCurrent</c> (0x1402033A3) tests
        /// <c>test byte ptr [unit+1EEh], 8</c> immediately before <c>ActivateGuideEntry</c>
        /// (the "your unit can be commanded" hint), and <c>DrawCharacterCombatTimeline_maybe</c>
        /// (0x140215540) treats the unit as controlled when
        /// <c>(Flags2_2 &amp; 0x30) != 0 || (Flags2_2 &amp; 8) != 0</c>. 14 per-field references to
        /// bit 0x08 of the mirror byte were found with <c>test byte ptr [reg+1EEh], 8</c>.
        /// </para>
        /// </summary>
        public const byte Flags2HumanControl = 0x08;

        /// <summary>
        /// Guest / special-unit mask inside <c>Flags1</c>.
        /// <para>
        /// CONFIRMED as a real game idiom, not an invented heuristic: <c>set_status_counter</c>
        /// (0x140278CFE), <c>unitwork_init2all</c> (0x140278E55) and <c>check_tobe_crystal</c>
        /// (0x14030FA73) all test <c>Flags2 &amp; 0x04</c> OR <c>Flags1 &amp; 0x09</c> to decide
        /// whether a unit is exempt from the death/crystal bookkeeping in <c>BattleUnit.DeathCounter</c>.
        /// </para>
        /// <para>
        /// Use together with <see cref="Flags2NonCrystalMask"/> to reproduce the game's predicate.
        /// </para>
        /// </summary>
        public const byte Flags1GuestMask = 0x09;

        /// <summary>
        /// The <c>Flags2</c> half of the game's guest/special-unit predicate. Test this against the
        /// <c>+0x05</c> copy of <c>Flags2</c>, which is what the game itself reads.
        /// </summary>
        public const byte Flags2NonCrystalMask = 0x04;
    }
}
