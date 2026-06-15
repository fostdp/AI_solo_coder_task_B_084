
(function(global) {
    'use strict';

    const EChartsOverlay = {
        _chartInstances: {},
        _lastDataHashes: {},
        _canvasCache: {},

        initChart: function(containerId, options) {
            if (
const el = document.getElementById(containerId);
            if (!el || !global.echarts) return null;

            if (this._chartInstances[containerId]?.dispose?.();
            const chart = echarts.init(el, null, { renderer: 'canvas' });
            this._chartInstances[containerId] = chart;

            const baseOptions = Object.assign({
                backgroundColor: 'transparent',
                animation: false,
                grid: { left: 45, right: 20, top: 30, bottom: 30 },
                tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
                legend: { data: [], top: 0, textStyle: { fontSize: 11 } }
            }, options || {});

            chart.setOption(baseOptions);
            return chart;
        },

        renderPredictionTrend: function(containerId, dataPoints, modelType) {
            if (!this._chartInstances[containerId])
                this.initChart(containerId);
            const chart = this._chartInstances[containerId];

            const dates = dataPoints.map(p => this._fmtDate(p.date));
            const holeSeries = dataPoints.map(p => p.predictedHoleDensity ?? null);
            const fungiSeries = dataPoints.map(p => p.predictedFungiCFU ?? null);
            const predatorSeries = dataPoints.map(p => p.predatorDensity ?? null);
            const predationSeries = dataPoints.map(p => p.predationRate ?? null);

            const legend = ['虫蛀密度预测 (H)', '霉菌浓度预测 (F)'];
            const series = [
                {
                    name: '虫蛀密度预测 (H)',
                    type: 'line',
                    smooth: true,
                    yAxisIndex: 0,
                    data: holeSeries,
                    lineStyle: { color: '#C62828', width: 2.5 },
                    itemStyle: { color: '#C62828' },
                    areaStyle: { color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                        { offset: 0, color: 'rgba(198,40,40,0.35)' },
                        { offset: 1, color: 'rgba(198,40,40,0.02)' }
                    ])},
                    symbol: 'circle', symbolSize: 5
                },
                {
                    name: '霉菌浓度预测 (F)',
                    type: 'line',
                    smooth: true,
                    yAxisIndex: 1,
                    data: fungiSeries,
                    lineStyle: { color: '#2E7D32', width: 2 },
                    itemStyle: { color: '#2E7D32' },
                    symbol: 'diamond', symbolSize: 5
                }
            ];

            if (predatorSeries.some(v => v !== null)) {
                legend.push('拟蝎种群 (P)');
                series.push({
                    name: '拟蝎种群 (P)',
                    type: 'line',
                    smooth: true,
                    yAxisIndex: 0,
                    data: predatorSeries,
                    lineStyle: { color: '#1565C0', width: 2, type: 'dashed' },
                    itemStyle: { color: '#1565C0' },
                    symbol: 'triangle', symbolSize: 4
                });
            }

            if (predationSeries.some(v => v !== null)) {
                legend.push('捕食率 (dH_pr)');
                series.push({
                    name: '捕食率 (dH_pr)',
                    type: 'bar',
                    yAxisIndex: 2,
                    data: predationSeries,
                    itemStyle: {
                        color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                            { offset: 0, color: 'rgba(255,193,7,0.7)' },
                            { offset: 1, color: 'rgba(255,152,0,0.2)' }
                        ]),
                        borderRadius: [4, 4, 0, 0]
                    },
                    barWidth: '30%'
                });
            }

            const hash = this._computeHash({ dates, holeSeries, fungiSeries, modelType });
            if (this._lastDataHashes[containerId] === hash) {
                return;
            }
            this._lastDataHashes[containerId] = hash;

            const option = {
                legend: { data: legend, top: 0, textStyle: { fontSize: 11 } },
                xAxis: {
                    type: 'category',
                    data: dates,
                    boundaryGap: predationSeries.some(v => v !== null),
                    axisLabel: { rotate: 30, fontSize: 10 },
                    axisLine: { lineStyle: { color: '#aaa' } }
                },
                yAxis: [
                    {
                        type: 'value',
                        name: 'H, P',
                        position: 'left',
                        nameTextStyle: { color: '#C62828', fontSize: 10 },
                        axisLabel: { color: '#C62828', fontSize: 10 },
                        splitLine: { lineStyle: { type: 'dashed', color: '#eee' } }
                    },
                    {
                        type: 'value',
                        name: 'F (CFU/cm²)',
                        position: 'right',
                        nameTextStyle: { color: '#2E7D32', fontSize: 10 },
                        axisLabel: { color: '#2E7D32', fontSize: 10 }
                    },
                    {
                        type: 'value',
                        name: '捕食率',
                        position: 'right',
                        offset: 65,
                        show: predationSeries.some(v => v !== null),
                        nameTextStyle: { color: '#FF9800', fontSize: 10 },
                        axisLabel: { color: '#FF9800', fontSize: 10 },
                        splitLine: { show: false }
                    }
                ],
                dataZoom: [
                    { type: 'inside', start: 0, end: 100 },
                    { type: 'slider', height: 18, bottom: 8 }
                ],
                series: series
            };

            chart.setOption(option, true);
            chart.resize();
        },

        renderOverlayMarkers: function(chart, overlayCanvas, textileCanvas, markers, moldRegions) {
            if (!textileCanvas || !overlayCanvas) return;
            const W = overlayCanvas.width;
            const H = overlayCanvas.height;
            const octx = overlayCanvas.getContext('2d');
            octx.clearRect(0, 0, W, H);

            if (moldRegions && typeof MildewCanvas !== 'undefined') {
                moldRegions.forEach((m, idx) => {
                    const mx = (m.relativeX || 0.2 + Math.random() * 0.6) * W;
                    const my = (m.relativeY || 0.2 + Math.random() * 0.6) * H;
                    const mr = Math.max(20, m.radius || 40);
                    MildewCanvas.drawMoldCloud(octx, mx, my, mr, (m.id || idx + 1) * 7, 0.85);
                });
            } else if (moldRegions) {
                moldRegions.forEach(m => {
                    const mx = (m.relativeX || 0.5) * W;
                    const my = (m.relativeY || 0.5) * H;
                    const mr = Math.max(20, m.radius || 40);
                    octx.beginPath();
                    octx.arc(mx, my, mr, 0, Math.PI * 2);
                    const grad = octx.createRadialGradient(mx, my, 0, mx, my, mr);
                    grad.addColorStop(0, 'rgba(107,142,35,0.6)');
                    grad.addColorStop(1, 'rgba(107,142,35,0)');
                    octx.fillStyle = grad;
                    octx.fill();
                });
            }

            if (markers) {
                markers.forEach(h => {
                    const hx = (h.relativeX || 0.5) * W;
                    const hy = (h.relativeY || 0.5) * H;
                    const hr = Math.max(3, h.radius || 6);
                    const sev = h.severityLevel || (Math.floor(Math.random() * 4) + 1);
                    const col = sev >= 4 ? '#C62828' : sev >= 3 ? '#E65100' : sev >= 2 ? '#F57C00' : '#FFB74D';
                    octx.beginPath();
                    octx.arc(hx, hy, hr, 0, Math.PI * 2);
                    const holeGrad = octx.createRadialGradient(hx, hy, 0, hx, hy, hr);
                    holeGrad.addColorStop(0, 'rgba(0,0,0,0.9)');
                    holeGrad.addColorStop(1, 'rgba(0,0,0,0.7)');
                    octx.fillStyle = holeGrad;
                    octx.fill();
                    octx.beginPath();
                    octx.arc(hx, hy, hr + 1, 0, Math.PI * 2);
                    octx.strokeStyle = col;
                    octx.lineWidth = sev >= 3 ? 2.5 : 1.5;
                    octx.stroke();
                });
            }
        },

        resizeAll: function() {
            Object.values(this._chartInstances).forEach(c => c?.resize());
        },

        disposeAll: function() {
            Object.values(this._chartInstances).forEach(c => c?.dispose());
            this._chartInstances = {};
            this._lastDataHashes = {};
            this._canvasCache = {};
        },

        _fmtDate: function(d) {
            if (!d) return '';
            const dt = typeof d === 'string' ? new Date(d) : d;
            const m = String(dt.getMonth() + 1).padStart(2, '0');
            const day = String(dt.getDate()).padStart(2, '0');
            return `${dt.getFullYear()}-${m}-${day}`;
        },

        _computeHash: function(obj) {
            const s = typeof obj === 'string' ? obj : JSON.stringify(obj);
            let h = 0;
            for (let i = 0; i < s.length; i++) {
                h = ((h << 5) - h) + s.charCodeAt(i);
                h |= 0;
            }
            return h;
        }
    };

    global.EChartsOverlay = EChartsOverlay;

})(typeof window !== 'undefined' ? window : this);
