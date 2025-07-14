// ============================================================================
// MNFSTPANELv11 – Signal History Dot Panel
// Developed by: Nick Powell on 7/14/25 in coordination with ChatGPT
// ----------------------------------------------------------------------------
// PURPOSE:
// Display historical dot markers for:
// - VWAP GATE
// - OBV Slope (previous bar)
// - CEI Flip Trigger (from 2 bars ago to 1 bar ago)
// - TRADE SIGNAL (when all conditions align)
// ============================================================================

#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui.Chart;
using SharpDX;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MNFSTPANELv11 : Indicator
    {
        private Series<string> vwapStatus, obvStatus, ceiStatus, triggerStatus;
        private Series<double> obvSeries, obvEmaSeries, ceiSeries, vwapSeries;

        [NinjaScriptProperty]
        public int Lookback { get; set; } = 34;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Plots historical dots for VWAP, OBV, CEI, and Entry Trigger.";
                Name = "MNFSTPANELv11";
                IsOverlay = false;
            }
            else if (State == State.DataLoaded)
            {
                vwapStatus    = new Series<string>(this);
                obvStatus     = new Series<string>(this);
                ceiStatus     = new Series<string>(this);
                triggerStatus = new Series<string>(this);
                obvSeries     = new Series<double>(this);
                obvEmaSeries  = new Series<double>(this);
                ceiSeries     = new Series<double>(this);
                vwapSeries    = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Lookback + 2)
                return;

            // Time check
            int t = Time[0].Hour * 100 + Time[0].Minute;
            bool timeOK = (t >= 745 && t <= 1100);

            // --- VWAP GATE LOGIC ---
            double pv = 0, vv = 0;
            for (int i = 0; i < Lookback; i++)
            {
                pv += Close[i] * Volume[i];
                vv += Volume[i];
            }
            double vwap = vv == 0 ? Close[0] : pv / vv;
            vwapSeries[0] = vwap;

            double sum = 0, sum2 = 0;
            for (int i = 0; i < Lookback; i++)
            {
                sum += Close[i];
                sum2 += Close[i] * Close[i];
            }
            double mean = sum / Lookback;
            double stddev = Math.Sqrt(Math.Max((sum2 / Lookback) - (mean * mean), 0.0));

            double price = Close[0];

            if (!timeOK)
                vwapStatus[0] = "WAIT";
            else if ((price > vwap + stddev && price < vwap + 2 * stddev) || (price < vwap - stddev && price > vwap - 2 * stddev))
                vwapStatus[0] = "BLUE";  // gate open
            else
                vwapStatus[0] = "GRAY";  // gate closed or inside

            // --- OBV EMA SLOPE USING CLOSED BARS ---
            double delta = Close[0] - Close[1];
            if (delta > 0)
                obvSeries[0] = obvSeries[1] + Volume[0];
            else if (delta < 0)
                obvSeries[0] = obvSeries[1] - Volume[0];
            else
                obvSeries[0] = obvSeries[1];

            if (CurrentBar < Lookback)
                obvEmaSeries[0] = obvSeries[0];
            else
            {
                double alpha = 2.0 / (Lookback + 1);
                obvEmaSeries[0] = alpha * obvSeries[0] + (1 - alpha) * obvEmaSeries[1];
            }

            if (!timeOK)
                obvStatus[0] = "WAIT";
            else if (obvEmaSeries[1] > obvEmaSeries[2])
                obvStatus[0] = "GREEN";
            else if (obvEmaSeries[1] < obvEmaSeries[2])
                obvStatus[0] = "RED";
            else
                obvStatus[0] = "GRAY";

            // --- CEI INVERSION USING CLOSED BARS ---
            double eff = ((Close[0] - vwap) - (Close[1] - vwapSeries[1])) / Math.Max(1, Volume[0]);
            ceiSeries[0] = eff;

            double ceiSum = 0;
            for (int i = 0; i < Lookback; i++)
                ceiSum += ceiSeries[i];

            double ceiSlope = ceiSum - ceiSeries[Math.Min(Lookback, CurrentBar)];
            bool isCEIGreen = ceiSlope > 0;
            bool isCEIRed = ceiSlope < 0;

            if (!timeOK)
                ceiStatus[0] = "WAIT";
            else if (isCEIGreen)
                ceiStatus[0] = "GREEN";
            else if (isCEIRed)
                ceiStatus[0] = "RED";
            else
                ceiStatus[0] = "GRAY";

            // --- TRIGGER DOT: CEI must flip in OBV direction ---
            double ceiPrev = ceiSeries[2];
            double ceiNow  = ceiSeries[1];

            bool flippedGreen = ceiPrev < 0 && ceiNow > 0;
            bool flippedRed   = ceiPrev > 0 && ceiNow < 0;

            if (timeOK && vwapStatus[0] == "BLUE")
            {
                if (obvStatus[0] == "GREEN" && flippedGreen)
                    triggerStatus[0] = "GREEN";
                else if (obvStatus[0] == "RED" && flippedRed)
                    triggerStatus[0] = "RED";
                else
                    triggerStatus[0] = "GRAY";
            }
            else
            {
                triggerStatus[0] = "GRAY";
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            float dotRadius = 3.5f;
            float rowSpacing = 15f;
            float yStart = (float)ChartPanel.Y + 20f;

            for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
            {
                float x = chartControl.GetXByBarIndex(ChartBars, idx);

                for (int row = 0; row < 4; row++)
                {
                    string signal = row == 0 ? vwapStatus.GetValueAt(idx)
                                 : row == 1 ? obvStatus.GetValueAt(idx)
                                 : row == 2 ? ceiStatus.GetValueAt(idx)
                                 : triggerStatus.GetValueAt(idx);

                    SharpDX.Direct2D1.Brush brush = signal == "GREEN" ? BrushesToDx(Colors.LimeGreen)
                                                : signal == "RED" ? BrushesToDx(Colors.OrangeRed)
                                                : signal == "BLUE" ? BrushesToDx(Colors.DeepSkyBlue)
                                                : BrushesToDx(Colors.Gray);

                    float y = yStart + row * rowSpacing + (row >= 3 ? rowSpacing : 0);
                    RenderTarget.FillEllipse(new SharpDX.Direct2D1.Ellipse(new Vector2(x, y), dotRadius, dotRadius), brush);
                }
            }
        }

        private SharpDX.Direct2D1.SolidColorBrush BrushesToDx(System.Windows.Media.Color color)
        {
            return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(
                color.ScR == 0 ? color.R / 255f : color.ScR,
                color.ScG == 0 ? color.G / 255f : color.ScG,
                color.ScB == 0 ? color.B / 255f : color.ScB,
                color.ScA == 0 ? color.A / 255f : color.ScA));
        }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MNFSTPANELv11[] cacheMNFSTPANELv11;
		public MNFSTPANELv11 MNFSTPANELv11(int lookback)
		{
			return MNFSTPANELv11(Input, lookback);
		}

		public MNFSTPANELv11 MNFSTPANELv11(ISeries<double> input, int lookback)
		{
			if (cacheMNFSTPANELv11 != null)
				for (int idx = 0; idx < cacheMNFSTPANELv11.Length; idx++)
					if (cacheMNFSTPANELv11[idx] != null && cacheMNFSTPANELv11[idx].Lookback == lookback && cacheMNFSTPANELv11[idx].EqualsInput(input))
						return cacheMNFSTPANELv11[idx];
			return CacheIndicator<MNFSTPANELv11>(new MNFSTPANELv11(){ Lookback = lookback }, input, ref cacheMNFSTPANELv11);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MNFSTPANELv11 MNFSTPANELv11(int lookback)
		{
			return indicator.MNFSTPANELv11(Input, lookback);
		}

		public Indicators.MNFSTPANELv11 MNFSTPANELv11(ISeries<double> input , int lookback)
		{
			return indicator.MNFSTPANELv11(input, lookback);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MNFSTPANELv11 MNFSTPANELv11(int lookback)
		{
			return indicator.MNFSTPANELv11(Input, lookback);
		}

		public Indicators.MNFSTPANELv11 MNFSTPANELv11(ISeries<double> input , int lookback)
		{
			return indicator.MNFSTPANELv11(input, lookback);
		}
	}
}

#endregion
