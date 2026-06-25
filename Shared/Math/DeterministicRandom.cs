using System;

namespace Shared.Math
{
    /// <summary>
    /// 纯确定性伪随机数生成器 (基于 Xorshift32 算法)
    /// 专为帧同步设计，杜绝跨平台误差和系统底层实现差异
    /// </summary>
    public struct DeterministicRandom
    {
        // 内部状态，永远通过纯整数位运算演进
        private uint _state;

        /// <summary>
        /// 初始化随机数种子
        /// </summary>
        /// <param name="seed">必须是多端完全一致的数据组合 (例如: GameDays + 战斗ID)</param>
        public DeterministicRandom(uint seed)
        {
            // Xorshift 算法的种子绝对不能为 0，否则状态将永远卡在 0
            _state = seed == 0 ? 1 : seed;
        }

        /// <summary>
        /// 核心算法：生成下一个纯正整数
        /// </summary>
        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>
        /// 获取 [min, max] 范围内的随机整数 (包含 min 和 max 本身)
        /// </summary>
        public int Range(int min, int max)
        {
            if (min > max)
            {
                // 安全防御：如果传反了，自动交换
                (min, max) = (max, min);
            }
            
            // 计算区间跨度，使用无符号整数防止溢出
            uint range = (uint)(max - min + 1);
            
            // 取模返回
            return min + (int)(NextUInt() % range);
        }
    }
}