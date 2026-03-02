using System;

namespace Qingque13.Core
{
    /// <summary>
    /// Represents win type/conditions.
    /// Matches C++ win_t (uint16_t) and win_type class.
    /// Lower 8 bits contain win flags and wind info.
    /// Upper 8 bits available for custom data.
    /// </summary>
    public struct QingqueWinType : IEquatable<QingqueWinType>
    {
        private readonly ushort value;

        // Win condition flags
        public const ushort SelfDrawn = 0b00010000;              // 自摸
        public const ushort FinalTile = 0b00100000;              // 最后一张 (海底/河底)
        public const ushort KongRelated = 0b01000000;            // 杠相关 (岭上/抢杠)
        public const ushort HeavenlyOrEarthlyHand = 0b10000000;  // 天和/地和

        // Filters
        public const ushort SeatWindFilter = 0b00000011;         // 自风
        public const ushort PrevalentWindFilter = 0b00001100;    // 场风

        public QingqueWinType(ushort value)
        {
            this.value = value;
        }

        public QingqueWinType(
            bool selfDrawn = false,
            bool finalTile = false,
            bool kongRelated = false,
            bool heavenlyOrEarthlyHand = false,
            QingqueTile seatWind = default,
            QingqueTile prevalentWind = default,
            byte upperBits = 0)
        {
            ushort val = 0;

            if (selfDrawn) val |= SelfDrawn;
            if (finalTile) val |= FinalTile;
            if (kongRelated) val |= KongRelated;
            if (heavenlyOrEarthlyHand) val |= HeavenlyOrEarthlyHand;

            val |= (ushort)((upperBits & 0xFF) << 8);

            // Encode seat wind
            byte seatWindValue = seatWind.Value;
            if (seatWindValue == 0) seatWindValue = QingqueTile.Honours.E;
            switch (seatWindValue)
            {
                case QingqueTile.Honours.E: val |= 0b00000000; break;
                case QingqueTile.Honours.S: val |= 0b00000001; break;
                case QingqueTile.Honours.W: val |= 0b00000010; break;
                case QingqueTile.Honours.N: val |= 0b00000011; break;
            }

            // Encode prevalent wind
            byte prevalentWindValue = prevalentWind.Value;
            if (prevalentWindValue == 0) prevalentWindValue = QingqueTile.Honours.E;
            switch (prevalentWindValue)
            {
                case QingqueTile.Honours.E: val |= 0b00000000; break;
                case QingqueTile.Honours.S: val |= 0b00000100; break;
                case QingqueTile.Honours.W: val |= 0b00001000; break;
                case QingqueTile.Honours.N: val |= 0b00001100; break;
            }

            this.value = val;
        }

        public ushort Value => value;

        /// <summary>
        /// Checks if the specified flags are set.
        /// </summary>
        public bool HasFlags(ushort requiredFlags, ushort excludedFlags = 0)
        {
            return ((value & requiredFlags) == requiredFlags) && ((value & excludedFlags) == 0);
        }

        /// <summary>
        /// Gets the seat wind (自风).
        /// </summary>
        public QingqueTile SeatWind()
        {
            byte windCode = (byte)(value & SeatWindFilter);
            switch (windCode)
            {
                case 0: return new QingqueTile(QingqueTile.Honours.E);
                case 1: return new QingqueTile(QingqueTile.Honours.S);
                case 2: return new QingqueTile(QingqueTile.Honours.W);
                case 3: return new QingqueTile(QingqueTile.Honours.N);
                default: return new QingqueTile(QingqueTile.Honours.E);
            }
        }

        /// <summary>
        /// Gets the prevalent wind (场风).
        /// </summary>
        public QingqueTile PrevalentWind()
        {
            byte windCode = (byte)((value & PrevalentWindFilter) >> 2);
            switch (windCode)
            {
                case 0: return new QingqueTile(QingqueTile.Honours.E);
                case 1: return new QingqueTile(QingqueTile.Honours.S);
                case 2: return new QingqueTile(QingqueTile.Honours.W);
                case 3: return new QingqueTile(QingqueTile.Honours.N);
                default: return new QingqueTile(QingqueTile.Honours.E);
            }
        }

        /// <summary>
        /// Gets the upper 8 bits for custom data.
        /// </summary>
        public byte UpperBits => (byte)(value >> 8);

        public bool IsSelfDrawn => (value & SelfDrawn) != 0;
        public bool IsFinalTile => (value & FinalTile) != 0;
        public bool IsKongRelated => (value & KongRelated) != 0;
        public bool IsHeavenlyOrEarthly => (value & HeavenlyOrEarthlyHand) != 0;

        public static implicit operator ushort(QingqueWinType winType) => winType.value;

        public static implicit operator QingqueWinType(ushort value) => new QingqueWinType(value);

        public bool Equals(QingqueWinType other) => value == other.value;

        public override bool Equals(object obj) => obj is QingqueWinType winType && Equals(winType);

        public override int GetHashCode() => value.GetHashCode();

        public static bool operator ==(QingqueWinType left, QingqueWinType right) => left.Equals(right);

        public static bool operator !=(QingqueWinType left, QingqueWinType right) => !left.Equals(right);

        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (IsSelfDrawn) parts.Add("SelfDrawn");
            if (IsFinalTile) parts.Add("FinalTile");
            if (IsKongRelated) parts.Add("KongRelated");
            if (IsHeavenlyOrEarthly) parts.Add("HeavenlyOrEarthly");
            parts.Add($"SeatWind:{SeatWind()}");
            parts.Add($"PrevalentWind:{PrevalentWind()}");
            return string.Join("|", parts);
        }
    }
}
