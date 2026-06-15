
window.VOC_NAMES = ['乙醇', '乙酸', '1-辛烯-3-醇', '2-甲基异茨醇', '土臭素', '异戊醛', '苯甲醛', '正己醛', '芳樟醇'];
window.VOC_UNITS = ['PPB', 'PPB', 'PPT', 'PPT', 'PPT', 'PPB', 'PPB', 'PPB', 'PPT'];
window.VOC_COLORS = ['#c0392b', '#e67e22', '#f39c12', '#556b2f', '#7cb342', '#1e6091', '#8e44ad', '#8e2f3f', '#a0522d'];
window.VOC_THRESHOLDS = [500, 200, 50, 20, 30, 100, 80, 150, 40];
window.MOLD_NAMES = ['黑曲霉', '产黄青霉', '多主枝孢', '链格孢', '绿色木霉', '禾谷镰刀菌'];
window.MOLD_COLORS = ['#2c3e50', '#556b2f', '#8e2f3f', '#a0522d', '#7cb342', '#c0392b'];

window.renderVocStackedArea = function(domId, timeSeriesData) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    const series = window.VOC_NAMES.map((name, i) => ({
        name: name,
        type: 'line',
        stack: 'Total',
        smooth: true,
        symbol: 'none',
        emphasis: { focus: 'series' },
        lineStyle: { width: 1, color: window.VOC_COLORS[i] },
        itemStyle: { color: window.VOC_COLORS[i] },
        areaStyle: {
            color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                { offset: 0, color: window.VOC_COLORS[i] + 'cc' },
                { offset: 1, color: window.VOC_COLORS[i] + '10' }
            ])
        },
        yAxisIndex: window.VOC_UNITS[i] === 'PPT' ? 1 : 0,
        data: timeSeriesData.values[i]
    }));
    chart.setOption({
        tooltip: { trigger: 'axis' },
        legend: { data: window.VOC_NAMES, top: 0, textStyle: { fontSize: 10 }, type: 'scroll' },
        grid: { left: 60, right: 60, top: 50, bottom: 60 },
        xAxis: { type: 'category', boundaryGap: false, data: timeSeriesData.times, axisLabel: { fontSize: 10, rotate: 45, interval: Math.floor(timeSeriesData.times.length / 12) } },
        yAxis: [
            { type: 'value', name: 'PPB级', axisLabel: { color: '#c0392b', fontSize: 10 }, nameTextStyle: { color: '#c0392b', fontSize: 11 }, position: 'left' },
            { type: 'value', name: 'PPT级', axisLabel: { color: '#1e6091', fontSize: 10 }, nameTextStyle: { color: '#1e6091', fontSize: 11 }, position: 'right' }
        ],
        dataZoom: [{ type: 'inside' }, { type: 'slider', height: 18, bottom: 5 }],
        series: series
    });
};

window.renderMoldSpeciesDonut = function(domId, stats) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    chart.setOption({
        tooltip: { trigger: 'item', formatter: '{b}: {c} CFU ({d}%)' },
        legend: { orient: 'vertical', left: 5, top: 'center', textStyle: { fontSize: 11 } },
        title: { text: '6种霉菌分布', left: 'center', top: 10, textStyle: { fontSize: 13, color: '#333' } },
        series: [{
            type: 'pie',
            radius: ['45%', '75%'],
            center: ['65%', '55%'],
            avoidLabelOverlap: true,
            itemStyle: { borderRadius: 6, borderColor: '#fff', borderWidth: 2 },
            label: { show: true, formatter: '{b}\n{d}%', fontSize: 10 },
            emphasis: { label: { show: true, fontSize: 12, fontWeight: 'bold' } },
            data: window.MOLD_NAMES.map((name, i) => ({
                name: name,
                value: stats[i],
                itemStyle: { color: window.MOLD_COLORS[i] }
            }))
        }]
    });
};

window.renderVocRadar = function(domId, avgLevels) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    const normalized = avgLevels.map((v, i) => Math.min(100, (v / window.VOC_THRESHOLDS[i]) * 100));
    const thresholdData = Array(9).fill(100);
    chart.setOption({
        tooltip: { trigger: 'item' },
        legend: { data: ['当前水平', '阈值线(100%)'], bottom: 0, textStyle: { fontSize: 11 } },
        radar: {
            indicator: window.VOC_NAMES.map((name, i) => ({
                name: `${name}(${window.VOC_UNITS[i]})`,
                max: 120
            })),
            radius: '68%',
            axisName: { color: '#333', fontSize: 11 }
        },
        series: [{
            type: 'radar',
            data: [
                {
                    value: normalized,
                    name: '当前水平',
                    itemStyle: { color: '#c0392b' },
                    lineStyle: { color: '#c0392b', width: 2.5 },
                    areaStyle: {
                        color: new echarts.graphic.RadialGradient(0.5, 0.5, 1, [
                            { offset: 0, color: 'rgba(192,57,43,0.05)' },
                            { offset: 1, color: 'rgba(192,57,43,0.35)' }
                        ])
                    }
                },
                {
                    value: thresholdData,
                    name: '阈值线(100%)',
                    itemStyle: { color: '#f39c12' },
                    lineStyle: { color: '#f39c12', width: 2, type: 'dashed' },
                    symbol: 'none',
                    areaStyle: { color: 'rgba(243,156,18,0.08)' }
                }
            ]
        }]
    });
};

window.renderMycotoxinRiskGauge = function(domId, riskIndex) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    const color = riskIndex >= 75 ? '#c0392b' : riskIndex >= 50 ? '#e67e22' : riskIndex >= 25 ? '#f39c12' : '#556b2f';
    const label = riskIndex >= 75 ? '极高风险' : riskIndex >= 50 ? '高风险' : riskIndex >= 25 ? '中风险' : '低风险';
    chart.setOption({
        series: [{
            type: 'gauge',
            startAngle: 210,
            endAngle: -30,
            min: 0,
            max: 100,
            splitNumber: 5,
            itemStyle: { color: color, borderColor: '#fff', borderWidth: 2 },
            progress: { show: true, width: 22, roundCap: true },
            pointer: { icon: 'path://M2090.36389,615.30999 L2090.36389,615.30999 C2091.48372,615.30999 2092.40383,616.194028 2092.44859,617.312956 L2096.90698,728.755929 C2097.05155,732.369577 2094.2393,735.416212 2090.62566,735.56078 C2090.53845,735.564269 2090.45117,735.566014 2090.36389,735.566014 L2090.36389,735.566014 C2086.74736,735.566014 2083.82299,732.631706 2083.82299,729.015172 L2083.82299,729.015172 L2085.27504,617.863734 C2085.32065,616.692881 2086.24184,615.758892 2087.41176,615.712814 L2087.41176,615.712814 C2088.10291,615.685621 2088.75147,615.965507 2089.20858,616.440051 C2089.66568,616.914595 2089.9762,617.562054 2090.07877,618.264224 L2090.07877,618.264224 C2090.24215,616.564897 2091.73073,615.30999 2090.36389,615.30999 Z', length: '75%', width: 12, offsetCenter: [0, '5%'] },
            axisLine: {
                roundCap: true,
                lineStyle: { width: 22, color: [[0.25, '#556b2f'], [0.5, '#f39c12'], [0.75, '#e67e22'], [1, '#c0392b']] }
            },
            axisTick: { distance: -30, splitNumber: 5, lineStyle: { width: 1, color: '#fff' } },
            splitLine: { distance: -35, length: 10, lineStyle: { width: 2, color: '#fff' } },
            axisLabel: { distance: -20, color: '#999', fontSize: 10 },
            anchor: { show: true, size: 20, itemStyle: { borderWidth: 2, borderColor: '#fff', color: color } },
            title: { offsetCenter: [0, '30%'], fontSize: 14, color: '#666' },
            detail: {
                valueAnimation: true,
                width: '60%',
                lineHeight: 40,
                borderRadius: 8,
                offsetCenter: [0, '-5%'],
                fontSize: 36,
                fontWeight: 'bolder',
                formatter: '{value}',
                color: color
            },
            data: [{ value: +riskIndex.toFixed(1), name: label }]
        }]
    });
};

window.renderBiomassPredictLine = function(domId, prediction) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    chart.setOption({
        tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
        legend: { data: ['实际观测', 'Gompertz预测', '95%置信区间'], top: 0, textStyle: { fontSize: 11 } },
        grid: { left: 55, right: 20, top: 40, bottom: 40 },
        xAxis: { type: 'category', data: prediction.dates, boundaryGap: false, axisLabel: { rotate: 30, fontSize: 10, interval: Math.floor(prediction.dates.length / 8) } },
        yAxis: { type: 'value', name: '生物量指数', nameTextStyle: { fontSize: 11 } },
        series: [
            {
                name: '95%置信区间',
                type: 'line',
                data: prediction.upper,
                stack: 'confidence-band',
                symbol: 'none',
                lineStyle: { opacity: 0 },
                areaStyle: { color: 'rgba(142,47,63,0.08)' }
            },
            {
                name: '95%置信区间下限',
                type: 'line',
                data: prediction.lower.map((v, i) => prediction.upper[i] - v),
                stack: 'confidence-band',
                symbol: 'none',
                lineStyle: { opacity: 0 },
                areaStyle: { color: 'rgba(255,255,255,1)' }
            },
            {
                name: '实际观测',
                type: 'line',
                data: prediction.actual,
                symbol: 'circle',
                symbolSize: 6,
                itemStyle: { color: '#2c3e50' },
                lineStyle: { color: '#2c3e50', width: 2 }
            },
            {
                name: 'Gompertz预测',
                type: 'line',
                smooth: true,
                data: prediction.predicted,
                symbol: 'diamond',
                symbolSize: 5,
                itemStyle: { color: '#c0392b' },
                lineStyle: { color: '#c0392b', width: 3 },
                markArea: {
                    silent: true,
                    data: [
                        [{ xAxis: prediction.warningStart, itemStyle: { color: 'rgba(243,156,18,0.08)' } }, {}]
                    ]
                }
            }
        ]
    });
};

window.generateVocMockData = function() {
    const times = [];
    for (let i = 23; i >= 0; i--) {
        const d = new Date(); d.setHours(d.getHours() - i);
        times.push(`${String(d.getHours()).padStart(2, '0')}:00`);
    }
    const vocValues = window.VOC_NAMES.map((name, i) => {
        const isPpt = window.VOC_UNITS[i] === 'PPT';
        const base = isPpt ? 5 + Math.random() * 15 : 50 + Math.random() * 100;
        let cur = base;
        return times.map(() => {
            cur += (Math.random() - 0.45) * (isPpt ? 3 : 15);
            cur = Math.max(isPpt ? 1 : 10, cur);
            return +cur.toFixed(2);
        });
    });
    const moldStats = window.MOLD_NAMES.map(() => Math.floor(50 + Math.random() * 500));
    const avgLevels = window.VOC_NAMES.map((_, i) => {
        const arr = vocValues[i];
        return arr.reduce((a, b) => a + b, 0) / arr.length;
    });
    const riskIndex = Math.min(100, avgLevels.reduce((s, v, i) => s + (v / window.VOC_THRESHOLDS[i]) * 100 / 9, 0) * (0.9 + Math.random() * 0.3));
    const bioDates = [];
    for (let i = 59; i >= 0; i--) {
        const d = new Date(); d.setDate(d.getDate() - i);
        bioDates.push(`${d.getMonth() + 1}/${d.getDate()}`);
    }
    const splitIdx = 20;
    let A = 95, B = 3.5, C = 0.08;
    const predicted = bioDates.map((_, i) => +(A * Math.exp(-B * Math.exp(-C * i))).toFixed(2));
    const actual = bioDates.map((_, i) => i < splitIdx ? +(predicted[i] * (0.92 + Math.random() * 0.16)).toFixed(2) : null);
    const upper = predicted.map(v => +(v * 1.15).toFixed(2));
    const lower = predicted.map(v => +(v * 0.85).toFixed(2));
    return {
        stacked: { times, values: vocValues },
        donut: moldStats,
        radar: avgLevels,
        gauge: riskIndex,
        biomass: {
            dates: bioDates,
            predicted: predicted,
            actual: actual,
            upper: upper,
            lower: lower,
            warningStart: bioDates[40]
        }
    };
};

window.initVocMonitoringView = function() {
    const data = window.generateVocMockData();
    window.renderVocStackedArea('vocStackedAreaChart', data.stacked);
    window.renderMoldSpeciesDonut('moldSpeciesDonutChart', data.donut);
    window.renderVocRadar('vocRadarChart', data.radar);
    window.renderMycotoxinRiskGauge('mycotoxinRiskGauge', data.gauge);
    window.renderBiomassPredictLine('biomassPredictLineChart', data.biomass);
};
