namespace AI.Caller.Core.Media {
    /// <summary>
    /// DTMF按键常量定义
    /// </summary>
    public static class DtmfKeys {
        public const byte Key0 = 0;
        public const byte Key1 = 1;
        public const byte Key2 = 2;
        public const byte Key3 = 3;
        public const byte Key4 = 4;
        public const byte Key5 = 5;
        public const byte Key6 = 6;
        public const byte Key7 = 7;
        public const byte Key8 = 8;
        public const byte Key9 = 9;
        public const byte KeyStar = 10;  // * 键
        public const byte KeyHash = 11;  // # 键

        /// <summary>
        /// 将DTMF按键值转换为字符
        /// </summary>
        public static char ToChar(byte key) {
            return key switch {
                0 => '0',
                1 => '1',
                2 => '2',
                3 => '3',
                4 => '4',
                5 => '5',
                6 => '6',
                7 => '7',
                8 => '8',
                9 => '9',
                10 => '*',
                11 => '#',
                _ => throw new ArgumentException($"Invalid DTMF key: {key}")
            };
        }

        /// <summary>
        /// 将字符转换为DTMF按键值
        /// </summary>
        public static byte FromChar(char c) {
            return c switch {
                '0' => 0,
                '1' => 1,
                '2' => 2,
                '3' => 3,
                '4' => 4,
                '5' => 5,
                '6' => 6,
                '7' => 7,
                '8' => 8,
                '9' => 9,
                '*' => 10,
                '#' => 11,
                _ => throw new ArgumentException($"Invalid DTMF character: {c}")
            };
        }

        /// <summary>
        /// 判断是否为有效的DTMF按键
        /// </summary>
        public static bool IsValid(byte key) {
            return key >= 0 && key <= 11;
        }

        /// <summary>
        /// 判断是否为数字键（0-9）
        /// </summary>
        public static bool IsDigit(byte key) {
            return key >= 0 && key <= 9;
        }
    }
}
