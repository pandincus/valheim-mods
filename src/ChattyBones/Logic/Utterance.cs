using System;

namespace ChattyBones.Logic
{
    /// <summary>
    /// One thing a skeleton said, in the form other players can be told about.
    /// </summary>
    /// <remarks>
    /// In multiplayer we have to tell other players what our skeletons said, because
    /// they never find out on their own. Every event we hook resolves only on the
    /// client owning the skeleton's ZDO: `BaseAI.UpdateAI` returns early when it is
    /// not the owner, and `ZNetView.InvokeRPC` routes to `m_zdo.GetOwner()` alone,
    /// which is how both `RPC_Damage` and `RPC_AddStatusEffect` get there.
    ///
    /// We send a line ref rather than a line index, so a listener whose pack is
    /// bigger than ours still draws from all of it. See LineChooser.LineRefFor.
    ///
    /// Counter, event and line ref pack into a single int, which is what this struct
    /// is for. A ZDO field is the cheapest way to reach exactly the clients who can
    /// see the skeleton: they already replicate to everyone in range, vanilla clients
    /// ignore keys they do not recognise, and we write no network code. A custom RPC
    /// would have every unmodded player logging a "not found" warning per quip.
    ///
    /// <see cref="Subject"/> travels in its own field - a prefab hash wants all 32
    /// bits to itself.
    /// </remarks>
    internal readonly struct Utterance
    {
        // How many bits of the packed int each part gets. Byte-aligned on purpose:
        // it costs a couple of bits we could have spent elsewhere, and buys being
        // able to read a packed value in hex and see straight away which part is
        // which. (A doc comment here would have attached to LineRefBits alone.)
        private const int LineRefBits = 16;
        private const int KindBits = 8;
        private const int CounterBits = 8;

        private const int LineRefMask = (1 << LineRefBits) - 1;
        private const int KindMask = (1 << KindBits) - 1;
        private const int CounterMask = (1 << CounterBits) - 1;

        /// <summary>The largest line ref that fits, so 65535.</summary>
        internal const int MaxLineRef = LineRefMask;

        /// <summary>
        /// Ticks up every time the skeleton says something, so watchers spot the change.
        /// </summary>
        /// <remarks>
        /// Other clients cannot subscribe to a ZDO field; they poll it. So the value
        /// has to differ from the last one they saw, even when the same skeleton says
        /// the same kind of thing about the same target twice running.
        ///
        /// It runs 1..255 and then wraps back to 1, never 0 - see
        /// <see cref="NextCounter"/>. A packed value of exactly 0 is what a ZDO field
        /// reads as when nobody has written it, so reserving 0 is what lets us tell
        /// "has not spoken" from "said the first line about a Summoned event".
        /// Without it every skeleton would greet you again on every client that came
        /// into range.
        /// </remarks>
        internal int Counter { get; }

        /// <summary>What happened.</summary>
        internal ChatterEvent Kind { get; }

        /// <summary>Which line to say.</summary>
        /// <remarks>
        /// Not an index - fold it with <c>% count</c> against whatever pack you have.
        /// The same pack on both sides gives the same line; a different pack gives
        /// something sensible out of its own file. Nothing random about it, despite
        /// how it is arrived at - see <c>LineChooser.LineRefFor</c>.
        /// </remarks>
        internal int LineRef { get; }

        /// <summary>
        /// What the event was about, or 0 when it was not about anything.
        /// </summary>
        /// <remarks>
        /// A creature's prefab hash, so the receiving client can look up its own
        /// localised name rather than us shipping the text across. Whoever is
        /// reading gets "Greydwarf" or "Grauzwerg" according to their own settings,
        /// which is a nice side effect of sending the hash rather than the word.
        ///
        /// Not part of <see cref="Pack"/> - see the note on the struct.
        /// </remarks>
        internal int Subject { get; }

        /// <summary>Build an utterance from its parts.</summary>
        /// <param name="counter">1 to 255. See <see cref="Counter"/> for why not 0.</param>
        /// <param name="kind">What happened.</param>
        /// <param name="lineRef">0 to <see cref="MaxLineRef"/>.</param>
        /// <param name="subject">A prefab hash, or 0 for events that are not about anything.</param>
        /// <remarks>
        /// This throws on a bad value, and <see cref="TryUnpack"/> never does. That
        /// split is the point of both. Values arriving here come from our own code,
        /// so one out of range is a bug in the mod and should be loud the first time
        /// it is run. Values arriving at TryUnpack come off the network from a client
        /// we do not control, so a bad one there is a Tuesday and gets a quiet no.
        ///
        /// Both of the checks below were originally absent, and both were quiet
        /// disasters. A counter of 0 packs the whole value to 0, which TryUnpack
        /// reads as "nobody has ever spoken" - so the utterance would vanish rather
        /// than fail. And a line ref above <see cref="MaxLineRef"/> silently loses its top
        /// bits, so remote clients would fold a different number and say a different
        /// line: the exact desync this whole type exists to prevent, arriving with no
        /// symptom at all on the machine that caused it.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// If <paramref name="counter"/> is outside 1..255, <paramref name="lineRef"/> is
        /// outside 0..<see cref="MaxLineRef"/>, or <paramref name="kind"/> does not fit
        /// in a byte.
        /// </exception>
        internal Utterance(int counter, ChatterEvent kind, int lineRef, int subject)
        {
            if (counter is < 1 or > CounterMask)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(counter), counter, "Counter runs 1.." + CounterMask + "; 0 would pack to a value meaning 'never spoken'.");
            }

            if (lineRef is < 0 or > MaxLineRef)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lineRef), lineRef, "LineRef must fit in " + LineRefBits + " bits, so 0.." + MaxLineRef + ".");
            }

            // Pack masks the event down to a byte, so a value of 256 or more would
            // arrive at the other end as a *different event* - the same silent desync
            // the line ref check above exists to stop. We are 244 events away from that
            // mattering, but a remark claiming these guards are the complete set
            // ought to be true.
            if ((int)kind is < 0 or > KindMask)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, "Event must fit in " + KindBits + " bits, so 0.." + KindMask + ".");
            }

            Counter = counter;
            Kind = kind;
            LineRef = lineRef;
            Subject = subject;
        }

        /// <summary>Squeeze the counter, event and line ref into one int.</summary>
        /// <returns>
        /// Counter in the top byte, event in the next, line ref in the bottom two.
        /// Never 0 for a validly built utterance, because the counter never is.
        ///
        /// Worked example - counter 7, TargetAcquired (event 1), line ref 4242:
        ///
        ///     7    &lt;&lt; 24    0x0700_0000
        ///     1    &lt;&lt; 16    0x0001_0000
        ///     4242             0x0000_1092
        ///     OR               0x0701_1092
        ///
        /// <see cref="TryUnpack"/> takes it apart again. There are tests walking every
        /// event, every counter in the full 1..255 cycle, and line refs up to
        /// <see cref="MaxLineRef"/> back out the far side.
        /// </returns>
        /// <remarks>
        /// The result is often negative, because a counter above 127 sets the top bit.
        /// That is fine - a ZDO int is signed and round-trips negatives happily.
        ///
        /// <see cref="TryUnpack"/> shifts as unsigned for the reader rather than for
        /// correctness. I assumed a signed shift would smear sign bits down into the
        /// event and line ref, wrote a test for a counter of 200 expecting it to fail,
        /// and it passed either way - every field is masked after the shift, and the
        /// mask discards the smeared bits. The cast stays because "shift an unsigned
        /// value" is obviously right at a glance.
        /// </remarks>
        internal int Pack()
        {
            uint packed = ((uint)(Counter & CounterMask) << (LineRefBits + KindBits))
                | ((uint)((int)Kind & KindMask) << LineRefBits)
                | (uint)(LineRef & LineRefMask);

            return (int)packed;
        }

        /// <summary>Take a packed value from the wire and make sense of it, if we can.</summary>
        /// <returns>
        /// False when the value is not something we can act on, in which case the
        /// caller should do nothing at all. There are two ways that happens:
        ///
        /// 1. The value is 0, meaning nobody has ever written to this field. Much
        ///    the most common case - it is what every skeleton looks like until the
        ///    first time it opens its mouth.
        /// 2. The event byte is not one we recognise. That means a client running a
        ///    newer version of this mod is telling us about an event that did not
        ///    exist when our copy was built.
        /// </returns>
        /// <param name="packed">The value read out of the ZDO.</param>
        /// <param name="subject">The prefab hash from its own field. Not validated - any int is legal, including 0.</param>
        /// <param name="utterance">The unpacked result, or default when we return false.</param>
        /// <remarks>
        /// Case 2 is the interesting one, and it is why this is a Try rather than
        /// something that throws. Multiplayer means our code has to read values
        /// written by a version of itself that we have never seen. If somebody
        /// running a later build adds a "skeleton stubbed its toe" event, everyone on
        /// the older build should quietly not react to it. They should not get an
        /// exception in the middle of a fight, and they should certainly not get a
        /// random line from whatever event happens to sit at that index.
        /// </remarks>
        internal static bool TryUnpack(int packed, int subject, out Utterance utterance)
        {
            utterance = default;

            if (packed == 0)
            {
                return false;
            }

            uint bits = (uint)packed;
            int counter = (int)((bits >> (LineRefBits + KindBits)) & CounterMask);
            int kind = (int)((bits >> LineRefBits) & KindMask);
            int lineRef = (int)(bits & LineRefMask);

            // Belt as well as braces. A counter of 0 with anything else set is not
            // something our own packing can produce, so it means the field was
            // written by something that is not us - a future version, or another mod
            // that happened to pick the same ZDO key. Either way we should not guess.
            if (counter == 0)
            {
                return false;
            }

            // Enum.IsDefined goes through reflection and is not something I would put
            // on a per-frame path. This runs when a watching client notices a ZDO
            // field changed, so a handful of times a second across the whole squad at
            // worst, and being robust to the enum growing later is worth far more
            // here than the microseconds.
            if (!Enum.IsDefined(typeof(ChatterEvent), kind))
            {
                return false;
            }

            utterance = new Utterance(counter, (ChatterEvent)kind, lineRef, subject);
            return true;
        }

        /// <summary>Work out the counter that follows this one.</summary>
        /// <returns>The next value in 1..255, wrapping back to 1 rather than to 0.</returns>
        /// <param name="previous">The counter last written, or 0 if nothing has been.</param>
        /// <remarks>
        /// Wrapping is harmless. A watcher only ever asks "is this different from what
        /// I saw last time", so the counter coming back around to a value it held 255
        /// utterances ago changes nothing - anyone who saw that one has long since
        /// seen 254 others.
        /// </remarks>
        internal static int NextCounter(int previous)
        {
            int next = (previous + 1) & CounterMask;
            return next == 0 ? 1 : next;
        }
    }
}
