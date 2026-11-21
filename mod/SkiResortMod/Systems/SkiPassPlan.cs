// Persisted per-area prices (T284, 1.0.4): on the Welcome Sign, the area's own day ticket, its own season pass plan (the next season's as
// the player set it) and the plan locked for the season whose sale has started (no price cuts within a season). The rules are pure:
// SkiPassPlanRules (Resolve, Sanitize, LockDue). A NEW type with a new SaveLayouts row (production mode: never a longer layout of a shipped
// type). Absent, or flags 0 with no lock for the season, = the Options default (the 1.0.3 behavior). An older build skips this unknown type
// cleanly (ComponentSerializer.cs:51-73, the SkiRulesStamp precedent): its prices then revert to the Options values.
// Writers, main thread only: SkiLiftTicketSystem (the lock at the season's sale start, on every live sign) and SkiResortHubUISystem (the hub's
// per-area setters). Readers: SkiLiftTicketSystem's per-sign price cache, which SkiDaySystem and SkiResortStatsSystem read.
// Layout v1 (36 bytes): m_Flags u8 | m_TicketPrice i32 | own plan: m_EarlyPrice i32, m_RisePct u8, m_SaleMask i32, m_RiseMask i32 |
// locked plan: m_LockSeason i32, m_LockEarly i32, m_LockRisePct u8, m_LockSaleMask i32, m_LockRiseMask i32. This layout never changes; a new
// field is a new type. Never rename the type or move its namespace without [FormerlySerializedAs].

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal. Rules: SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiPassPlan, SkiResortMod")]
    public struct SkiPassPlan : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>SkiPassPlanRules.FlagOwnTicket / FlagOwnPlan / FlagLockedDefault.</summary>
        public byte m_Flags;

        /// <summary>The area's own day ticket (read with FlagOwnTicket only).</summary>
        public int m_TicketPrice;

        /// <summary>The area's own plan (read with FlagOwnPlan only): the season it applies to is the next one whose sale starts.</summary>
        public int m_EarlyPrice;
        public byte m_RisePct;
        public int m_SaleMask;
        public int m_RiseMask;

        /// <summary>The season (SkiMoneyRules.SeasonKeyOfDate) whose sale started under the locked plan below. SkiPassPlanRules.NoLock = never.</summary>
        public int m_LockSeason;
        public int m_LockEarly;
        public byte m_LockRisePct;
        public int m_LockSaleMask;
        public int m_LockRiseMask;

        /// <summary>Layout version this value was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        /// <summary>A plan that follows the Options default in every part and has no lock (behaves as no component).</summary>
        public static SkiPassPlan FollowDefault()
        {
            return new SkiPassPlan
            {
                m_Flags = 0,
                m_LockSeason = SkiPassPlanRules.NoLock,
                m_Version = CurrentVersion,
            };
        }

        public SkiPassPlanRules.Plan Own => new SkiPassPlanRules.Plan(m_EarlyPrice, m_RisePct, m_SaleMask, m_RiseMask);
        public SkiPassPlanRules.Plan LockedPlan => new SkiPassPlanRules.Plan(m_LockEarly, m_LockRisePct, m_LockSaleMask, m_LockRiseMask);

        public void SetOwn(SkiPassPlanRules.Plan p)
        {
            m_EarlyPrice = p.Early;
            m_RisePct = (byte)p.RisePct;
            m_SaleMask = p.SaleMask;
            m_RiseMask = p.RiseMask;
        }

        public void SetLocked(int season, SkiPassPlanRules.Plan p)
        {
            m_LockSeason = season;
            m_LockEarly = p.Early;
            m_LockRisePct = (byte)p.RisePct;
            m_LockSaleMask = p.SaleMask;
            m_LockRiseMask = p.RiseMask;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            byte flags = m_Flags;
            writer.Write(flags);
            writer.Write(m_TicketPrice);
            writer.Write(m_EarlyPrice);
            byte rise = m_RisePct;
            writer.Write(rise);
            writer.Write(m_SaleMask);
            writer.Write(m_RiseMask);
            writer.Write(m_LockSeason);
            writer.Write(m_LockEarly);
            byte lockRise = m_LockRisePct;
            writer.Write(lockRise);
            writer.Write(m_LockSaleMask);
            writer.Write(m_LockRiseMask);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Flags);
            reader.Read(out m_TicketPrice);
            reader.Read(out m_EarlyPrice);
            reader.Read(out m_RisePct);
            reader.Read(out m_SaleMask);
            reader.Read(out m_RiseMask);
            reader.Read(out m_LockSeason);
            reader.Read(out m_LockEarly);
            reader.Read(out m_LockRisePct);
            reader.Read(out m_LockSaleMask);
            reader.Read(out m_LockRiseMask);
        }
    }
}
