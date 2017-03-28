using System;

namespace lib102
{
    /// <summary>
    /// 记账（计费）点能累计量
    /// <para>非连续</para>
    /// <para>信息对象地址+电能累计量+校核</para>
    /// <para>所有信息对象最后，增加一个5字节时标</para>
    /// </summary>
    public class IntegratedTotals : InformationObject
    {
        /// <summary>
        /// 类型标识M_IT_TA_2（2），记账电能累计量，每个量占4字节，表示范围：-99 999 999~+99 999 999
        /// </summary>
        override public TypeID Type
        {
            get
            {
                return TypeID.M_IT_TA_2;
            }
        }

        //不支持连续
        override public bool SupportsSequence
        {
            get
            {
                return false;
            }
        }

        #region 电能累计量IT
        //电能累计量结构：
        //---------------------------------
        //         八位位组1
        //---------------------------------
        //         八位位组2
        //---------------------------------
        //         八位位组3
        //---------------------------------
        //         八位位组4
        //---------------------------------
        //  IV | CA | CY | 顺序号（bit0-5）
        //---------------------------------


        /// <summary>
        /// 电能累计量数据，量程（-99 999 999~  +99 999 999），单位kWh
        /// </summary>
        public int Value;
        /// <summary>
        /// （CY）进位位 
        /// <para>true（1）：累计时间段内计数器溢出</para>
        /// <para>false（0）：累计时间段内计数器未溢出</para>
        /// </summary>
        public bool Carry;
        /// <summary>
        /// （CA）计数器调整位
        /// <para>true（1）：累计时间段内计数器被调整</para>
        /// <para>false（0）：累计时间段内计数器未被调整</para>
        /// </summary>
        public bool CounterAdjusted;
        /// <summary>
        /// （IV）无效位
        /// <para>true（1）：计数器读数无效</para>
        /// <para>false（0）：计数器读数有效</para>
        /// </summary>
        public bool Invalid;
        protected int serialNo;
        /// <summary>
        /// 顺序号,取值范围0-31
        /// <para>当电能累计量数据终端设备复位时，顺序号复位为0，一个累计时段改变时，顺序号加1</para>
        /// </summary>
        public int SerialNo
        {
            get
            {
                return serialNo;
            }
            set
            {
                if (value < 0) serialNo = 0;
                if (value > 31) serialNo = 31;
            }
        }
        #endregion

        /// <summary>
        /// 电能累计量的校核(仅用于类型标识2-7)
        /// </summary>
        protected byte checksum;

        /// <summary>
        /// 使用基本信息创建
        /// </summary>
        /// <param name="ioa"></param>
        /// <param name="val"></param>
        /// <param name="carry"></param>
        /// <param name="counterAdj"></param>
        /// <param name="invalid"></param>
        /// <param name="sn"></param>
        public IntegratedTotals(int ioa, int val,bool carry,bool counterAdj,bool invalid,int sn)
            : base(ioa)
        {
            this.Value = val;
            this.Carry = carry;
            this.CounterAdjusted = counterAdj;
            this.Invalid = invalid;
            this.SerialNo = sn;
        }

        internal IntegratedTotals( byte[] msg, int startIndex, bool isSquence) :
            base( msg, startIndex, isSquence)
        {
            if (!isSquence)
                startIndex ++; /* skip IOA */
            
        }

        internal override void Encode(Frame frame,  bool isSequence)
        {
            base.Encode(frame, isSequence);

        }
    }
}

