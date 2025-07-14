// ============================================================================
// MNFSTv11 – Multi-Factor Narrative Filtered Signal Trigger
// ----------------------------------------------------------------------------
// PURPOSE:
// Signal trade entries based on:
// - Time window restriction
// - VWAP distance gate (1 to 2 std devs)
// - OBV EMA directional slope
// - CEI slope-based inversion logic (matches child indicator coloring)
// - Visual status panel using SharpDX
// ============================================================================
#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using System.Windows.Media;
using SharpDX;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class MNFSTv11 : Indicator
    {
        [NinjaScriptProperty] public int OBVEMAPeriod { get; set; } = 34;
        [NinjaScriptProperty] public int CEILookback { get; set; } = 21;
        [NinjaScriptProperty] public int VWAPStdDevPeriod { get; set; } = 34;

        private Series<double> obvSeries, obvEmaSeries, ceiSeries;
        private Series<double> vwapSeries, stdDevSeries;
        private string sessionStatus = "OFF";
        private string locationStatus = "UNKNOWN";
        private string obvStatus = "NEUTRAL";
        private string ceiStatus = "WAIT";
        private int tradeSignal = 0;
        private SimpleFont dashboardFont, blockFont;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "MNFSTv11 Multi-Factor Narrative Filtered Signal Trigger";
                Name = "MNFSTv11";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
            }
            else if (State == State.DataLoaded)
            {
                obvSeries = new Series<double>(this);
                obvEmaSeries = new Series<double>(this);
                ceiSeries = new Series<double>(this);
                vwapSeries = new Series<double>(this);
                stdDevSeries = new Series<double>(this);
                dashboardFont = new SimpleFont("Segoe UI Semibold", 18);
                blockFont = new SimpleFont("Segoe UI Semibold", 24);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(Math.Max(OBVEMAPeriod, CEILookback), VWAPStdDevPeriod)) return;

            // --- Session Time Filter ---
            sessionStatus = IsSessionActive(Time[0]) ? "ON" : "OFF";

            // --- VWAP Distance Gate ---
            double vwap = CalcVWAP(VWAPStdDevPeriod);
            double stdDev = StdDev(Close, VWAPStdDevPeriod)[0];
            double price = Close[0];
            vwapSeries[0] = vwap;
            stdDevSeries[0] = stdDev;

            if (price > vwap - stdDev && price < vwap + stdDev)
                locationStatus = "INSIDE";
            else if ((price > vwap + stdDev && price < vwap + 2 * stdDev) || (price < vwap - stdDev && price > vwap - 2 * stdDev))
                locationStatus = "ACTIVE";
            else
                locationStatus = "OUTSIDE";

            // --- OBV EMA Slope (MATCHES CHILD INDICATOR) ---
            double delta = Close[0] - Close[1];
            if (delta > 0)
                obvSeries[0] = obvSeries[1] + Volume[0];
            else if (delta < 0)
                obvSeries[0] = obvSeries[1] - Volume[0];
            else
                obvSeries[0] = obvSeries[1];

            if (CurrentBar < OBVEMAPeriod)
                obvEmaSeries[0] = obvSeries[0];
            else
            {
                double alpha = 2.0 / (OBVEMAPeriod + 1);
                obvEmaSeries[0] = alpha * obvSeries[0] + (1 - alpha) * obvEmaSeries[1];
            }

            bool isOBVGreen = false;
            bool isOBVRed = false;

            if (CurrentBar > 0)
            {
                if (obvEmaSeries[0] > obvEmaSeries[1])
                    isOBVGreen = true;
                else if (obvEmaSeries[0] < obvEmaSeries[1])
                    isOBVRed = true;
            }

            obvStatus = isOBVGreen ? "UP" : isOBVRed ? "DOWN" : "NEUTRAL";

            // --- CEI Slope Inversion (Cumulative over Lookback) ---
            double eff = ((Close[0] - vwap) - (Close[1] - vwapSeries[1])) / Math.Max(1, Volume[0]);
            ceiSeries[0] = eff;

            double ceiCumulative = 0;
            for (int i = 0; i < CEILookback; i++)
                ceiCumulative += ceiSeries[i];

            double ceiSlope = ceiCumulative - ceiSeries[Math.Min(CurrentBar, CEILookback)];
            bool isCEIGreen = ceiSlope > 0;
            bool isCEIRed = ceiSlope < 0;

            // --- Signal Logic ---
            if (isCEIGreen && isOBVGreen)
            {
                ceiStatus = "LONG";
                tradeSignal = 1;
            }
            else if (isCEIRed && isOBVRed)
            {
                ceiStatus = "SHORT";
                tradeSignal = -1;
            }
            else
            {
                ceiStatus = "WAIT";
                tradeSignal = 0;
            }

            if (sessionStatus != "ON" || locationStatus != "ACTIVE")
                tradeSignal = 0;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            var dashFont = dashboardFont.ToDirectWriteTextFormat();
            var blkFont = blockFont.ToDirectWriteTextFormat();

            using (var sdBrushTextWhite = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.White)))
            using (var sdBrushTextLong = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.Lime)))
            using (var sdBrushTextShort = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.OrangeRed)))
            using (var sdBrushTextNo = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.Gray)))
            using (var sdBrushTextUp = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.Lime)))
            using (var sdBrushTextDown = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.OrangeRed)))
            using (var sdBrushTextNeutral = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.LightGray)))
            using (var sdBrushTextActive = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.Lime)))
            using (var sdBrushTextInside = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.CadetBlue)))
            using (var sdBrushTextOutside = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ToRawColor4(Colors.OrangeRed)))
            {
                float panelLeft = (float)ChartPanel.X + 20f;
                float panelTop = (float)ChartPanel.Y + 30f;
                float lineHeight = 32f;
                float boxW = 320f, boxH = 40f;

                RenderTarget.DrawText("MNFSTv11 STATUS", dashFont, new SharpDX.RectangleF(panelLeft, panelTop, boxW, boxH), sdBrushTextWhite);

                float colX = panelLeft;
                float colY = panelTop + lineHeight + 10f;

                var sessionBrush = sessionStatus == "ON" ? sdBrushTextLong : sdBrushTextShort;
                RenderTarget.DrawText($"Session: {sessionStatus}", dashFont, new SharpDX.RectangleF(colX, colY, boxW, boxH), sessionBrush);
                colY += lineHeight;

                var locBrush = locationStatus == "ACTIVE" ? sdBrushTextActive
                              : locationStatus == "INSIDE" ? sdBrushTextInside
                              : sdBrushTextOutside;
                RenderTarget.DrawText($"VWAP Gate: {locationStatus}", dashFont, new SharpDX.RectangleF(colX, colY, boxW, boxH), locBrush);
                colY += lineHeight;

                var obvBrush = obvStatus == "UP" ? sdBrushTextUp
                              : obvStatus == "DOWN" ? sdBrushTextDown
                              : sdBrushTextNeutral;
                RenderTarget.DrawText($"OBV: {obvStatus}", dashFont, new SharpDX.RectangleF(colX, colY, boxW, boxH), obvBrush);
                colY += lineHeight;

                var ceiBrush = ceiStatus == "LONG" ? sdBrushTextLong
                              : ceiStatus == "SHORT" ? sdBrushTextShort
                              : sdBrushTextNo;
                RenderTarget.DrawText($"CEI: {ceiStatus}", dashFont, new SharpDX.RectangleF(colX, colY, boxW, boxH), ceiBrush);

                float ellipseRadius = 200f;
                float ellipseX = (float)(ChartPanel.X + ChartPanel.W / 2);
                float ellipseY = (float)(ChartPanel.Y + ChartPanel.H - 10f);

                var ellipseBrush = sdBrushTextNo;
                if (tradeSignal == 1) ellipseBrush = sdBrushTextLong;
                else if (tradeSignal == -1) ellipseBrush = sdBrushTextShort;

                RenderTarget.FillEllipse(new SharpDX.Direct2D1.Ellipse(new Vector2(ellipseX, ellipseY), ellipseRadius, ellipseRadius), ellipseBrush);
                RenderTarget.DrawEllipse(new SharpDX.Direct2D1.Ellipse(new Vector2(ellipseX, ellipseY), ellipseRadius, ellipseRadius), sdBrushTextWhite, 3f);

                string label = tradeSignal == 1 ? "Become" : tradeSignal == -1 ? "Become" : "Let Go";
                float labelY = ellipseY - 50f;
                RenderTarget.DrawText(label, blkFont, new SharpDX.RectangleF(ellipseX - 28f, labelY, 80f, 40f), sdBrushTextWhite);
            }
        }

        private bool IsSessionActive(DateTime time)
        {
            int t = time.Hour * 100 + time.Minute;
            return (t >= 745 && t <= 1100);
        }

        private double CalcVWAP(int lookback)
        {
            double pv = 0, vv = 0;
            int bars = Math.Min(lookback, CurrentBar + 1);
            for (int i = 0; i < bars; i++)
            {
                pv += Close[i] * Volume[i];
                vv += Volume[i];
            }
            return vv == 0 ? Close[0] : pv / vv;
        }

        private static SharpDX.Color4 ToRawColor4(System.Windows.Media.Color color)
        {
            return new SharpDX.Color4(
                color.ScR == 0 ? color.R / 255f : color.ScR,
                color.ScG == 0 ? color.G / 255f : color.ScG,
                color.ScB == 0 ? color.B / 255f : color.ScB,
                color.ScA == 0 ? color.A / 255f : color.ScA);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MNFSTv11[] cacheMNFSTv11;
		public MNFSTv11 MNFSTv11(int oBVEMAPeriod, int cEILookback, int vWAPStdDevPeriod)
		{
			return MNFSTv11(Input, oBVEMAPeriod, cEILookback, vWAPStdDevPeriod);
		}

		public MNFSTv11 MNFSTv11(ISeries<double> input, int oBVEMAPeriod, int cEILookback, int vWAPStdDevPeriod)
		{
			if (cacheMNFSTv11 != null)
				for (int idx = 0; idx < cacheMNFSTv11.Length; idx++)
					if (cacheMNFSTv11[idx] != null && cacheMNFSTv11[idx].OBVEMAPeriod == oBVEMAPeriod && cacheMNFSTv11[idx].CEILookback == cEILookback && cacheMNFSTv11[idx].VWAPStdDevPeriod == vWAPStdDevPeriod && cacheMNFSTv11[idx].EqualsInput(input))
						return cacheMNFSTv11[idx];
			return CacheIndicator<MNFSTv11>(new MNFSTv11(){ OBVEMAPeriod = oBVEMAPeriod, CEILookback = cEILookback, VWAPStdDevPeriod = vWAPStdDevPeriod }, input, ref cacheMNFSTv11);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MNFSTv11 MNFSTv11(int oBVEMAPeriod, int cEILookback, int vWAPStdDevPeriod)
		{
			return indicator.MNFSTv11(Input, oBVEMAPeriod, cEILookback, vWAPStdDevPeriod);
		}

		public Indicators.MNFSTv11 MNFSTv11(ISeries<double> input , int oBVEMAPeriod, int cEILookback, int vWAPStdDevPeriod)
		{
			return indicator.MNFSTv11(input, oBVEMAPeriod, cEILookback, vWAPStdDevPeriod);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MNFSTv11 MNFSTv11(int oBVEMAPeriod, int cEILookback, int vWAPStdDevPeriod)
		{
			return indicator.MNFSTv11(Input, oBVEMAPeriod, cEILookback, vWAPStdDevPeriod);
		}

		public Indicators.MNFSTv11 MNFSTv11(ISeries<double> input , int oBVEMAPeriod, int cEILookback, int vWAPStdDevPeriod)
		{
			return indicator.MNFSTv11(input, oBVEMAPeriod, cEILookback, vWAPStdDevPeriod);
		}
	}
}

#endregion
