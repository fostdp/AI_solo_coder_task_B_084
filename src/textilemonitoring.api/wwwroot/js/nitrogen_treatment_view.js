
window.NITROGEN_STAGES = ['虫卵', '幼虫', '成虫', '真菌'];
window.NITROGEN_STAGE_COLORS = ['#1e6091', '#7cb342', '#f39c12', '#8e2f3f'];
window.HEATMAP_DOMS = ['mortalityHeatmapEgg', 'mortalityHeatmapLarva', 'mortalityHeatmapAdult', 'mortalityHeatmapFungi'];

window.calculateMortality = function(oxygenPct, exposureMin, stageIdx) {
    const stageSensitivity = [0.85, 1.15, 1.0, 0.7];
    const s = stageSensitivity[stageIdx] || 1;
    const oxygenFactor = Math.exp(-oxygenPct * 0.6 * s);
    const timeFactor = 1 - Math.exp(-exposureMin / (180 / s));
    const base = oxygenFactor * timeFactor * 100;
    const noise = (Math.random() - 0.5) * 6;
    return Math.max(0, Math.min(100, base + noise));
};

window.renderMortalityHeatmap = function(domId, oxygenRange, timeRange, stageIdx) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    const xLabels = timeRange;
    const yLabels = oxygenRange;
    const data = [];
    for (let yi = 0; yi < yLabels.length; yi++) {
        for (let xi = 0; xi < xLabels.length; xi++) {
            const m = window.calculateMortality(yLabels[yi], xLabels[xi], stageIdx);
            data.push([xi, yi, +m.toFixed(1)]);
        }
    }
    chart.setOption({
        tooltip: {
            position: 'top',
            formatter: function(p) {
                return `氧浓度: ${yLabels[p.value[1]]}%<br/>暴露时间: ${xLabels[p.value[0]]}min<br/>死亡率: <b>${p.value[2].toFixed(1)}%</b>`;
            }
        },
        grid: { left: 55, right: 25, top: 10, bottom: 45 },
        xAxis: {
            type: 'category',
            data: xLabels.map(t => t + ''),
            splitArea: { show: true },
            axisLabel: { rotate: 45, fontSize: 9, interval: Math.floor(xLabels.length / 8) },
            name: '暴露时间(min)',
            nameLocation: 'middle',
            nameGap: 30,
            nameTextStyle: { fontSize: 11 }
        },
        yAxis: {
            type: 'category',
            data: yLabels.map(o => o.toFixed(1)),
            splitArea: { show: true },
            axisLabel: { fontSize: 9 },
            name: 'O₂(%)',
            nameLocation: 'middle',
            nameGap: 38,
            nameTextStyle: { fontSize: 11 }
        },
        visualMap: {
            min: 0,
            max: 100,
            calculable: true,
            orient: 'horizontal',
            left: 'center',
            bottom: 0,
            itemWidth: 12,
            itemHeight: 120,
            textStyle: { fontSize: 10 },
            inRange: {
                color: ['#7cb342', '#cddc39', '#ffeb3b', '#ff9800', '#c0392b']
            }
        },
        series: [{
            name: window.NITROGEN_STAGES[stageIdx] + '死亡率',
            type: 'heatmap',
            data: data,
            label: { show: false },
            emphasis: {
                itemStyle: { shadowBlur: 10, shadowColor: 'rgba(0, 0, 0, 0.5)' }
            }
        }]
    });
};

window.probitFunc = function(x, alpha, beta) {
    const t = (Math.log(x) - alpha) / beta;
    return 100 * (1 / (1 + Math.exp(-1.702 * t)));
};

window.renderProbitCurve = function(domId, stage, exposureMinutes, oxygen, actualResult) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    const stageIdx = window.NITROGEN_STAGES.indexOf(stage);
    const stageAlphaBeta = [[1.2, 0.35], [0.9, 0.28], [1.0, 0.32], [1.5, 0.42]];
    const [alpha, beta] = stageAlphaBeta[stageIdx] || stageAlphaBeta[1];
    const oxFactor = Math.exp(-oxygen * 0.55);
    const dosePoints = [];
    for (let d = 10; d <= 2000; d += 20) {
        dosePoints.push(d);
    }
    const mortalityCurve = dosePoints.map(d => {
        const m = window.probitFunc(d * oxFactor, alpha + 0.2, beta);
        return [d, +m.toFixed(2)];
    });
    const ciWidth = dosePoints.map(d => 6 + 12 * Math.exp(-Math.pow((Math.log(d) - (alpha + 2)) / 1.5, 2)));
    const upperCurve = mortalityCurve.map((p, i) => [p[0], Math.min(100, +(p[1] + ciWidth[i]).toFixed(2))]);
    const lowerCurve = mortalityCurve.map((p, i) => [p[0], Math.max(0, +(p[1] - ciWidth[i]).toFixed(2))]);
    const actualX = exposureMinutes;
    const actualY = actualResult != null ? actualResult : window.calculateMortality(oxygen, exposureMinutes, stageIdx);
    chart.setOption({
        tooltip: {
            trigger: 'axis',
            axisPointer: { type: 'cross' },
            formatter: function(params) {
                let html = `暴露剂量: ${params[0]?.value?.[0] || 0} min<br/>`;
                params.forEach(p => {
                    if (p.seriesName !== '95%CI上限' && p.seriesName !== '95%CI下限') {
                        html += `${p.marker}${p.seriesName}: <b>${p.value?.[1]?.toFixed(2) || p.data?.toFixed(2) || 0}%</b><br/>`;
                    }
                });
                return html;
            }
        },
        legend: { data: ['Probit预测曲线', '95%置信区间', '当前参数实测点', 'LD₅₀', 'LD₉₉'], top: 0, textStyle: { fontSize: 11 } },
        grid: { left: 60, right: 20, top: 40, bottom: 50 },
        xAxis: {
            type: 'log',
            name: '暴露剂量 (min·低氧因子)',
            nameLocation: 'middle',
            nameGap: 32,
            nameTextStyle: { fontSize: 11 },
            min: 10,
            max: 2000,
            axisLabel: { fontSize: 10 }
        },
        yAxis: {
            type: 'value',
            name: '死亡率 (%)',
            nameTextStyle: { fontSize: 11 },
            min: 0,
            max: 102,
            axisLabel: { fontSize: 10 }
        },
        series: [
            {
                name: '95%置信区间',
                type: 'line',
                data: upperCurve,
                stack: 'ci',
                symbol: 'none',
                lineStyle: { opacity: 0 },
                areaStyle: { color: 'rgba(30,96,145,0.15)' }
            },
            {
                name: '95%CI下限',
                type: 'line',
                data: lowerCurve.map((p, i) => [p[0], upperCurve[i][1] - p[1]]),
                stack: 'ci',
                symbol: 'none',
                lineStyle: { opacity: 0 },
                areaStyle: { color: 'rgba(255,255,255,1)' }
            },
            {
                name: 'Probit预测曲线',
                type: 'line',
                smooth: false,
                data: mortalityCurve,
                symbol: 'none',
                itemStyle: { color: window.NITROGEN_STAGE_COLORS[stageIdx] },
                lineStyle: { color: window.NITROGEN_STAGE_COLORS[stageIdx], width: 3.5 }
            },
            {
                name: '当前参数实测点',
                type: 'scatter',
                data: [[actualX, +actualY.toFixed(2)]],
                symbol: 'diamond',
                symbolSize: 18,
                itemStyle: { color: '#c0392b', borderColor: '#fff', borderWidth: 2 }
            },
            {
                name: 'LD₅₀',
                type: 'line',
                markLine: {
                    silent: true,
                    symbol: 'none',
                    lineStyle: { color: '#a0522d', type: 'dashed', width: 2 },
                    label: { formatter: 'LD₅₀', color: '#a0522d', fontSize: 11, position: 'insideEndTop' },
                    data: [{ yAxis: 50 }]
                },
                data: []
            },
            {
                name: 'LD₉₉',
                type: 'line',
                markLine: {
                    silent: true,
                    symbol: 'none',
                    lineStyle: { color: '#8e2f3f', type: 'dashed', width: 2 },
                    label: { formatter: 'LD₉₉', color: '#8e2f3f', fontSize: 11, position: 'insideEndTop' },
                    data: [{ yAxis: 99 }]
                },
                data: []
            }
        ]
    });
};

window.renderTreatmentTimeline = function(domId, sessions) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    const categoryData = sessions.map(s => s.textileName);
    const barData = sessions.map(s => ({
        value: [sessions.indexOf(s), s.startIdx, s.endIdx],
        itemStyle: { color: s.success ? '#556b2f' : '#c0392b' }
    }));
    chart.setOption({
        tooltip: {
            formatter: function(params) {
                const s = sessions[params.value[0]];
                return `<b>${s.textileName}</b><br/>治疗时间: ${s.dateStr}<br/>持续: ${(s.endIdx - s.startIdx) / 60} 小时<br/>O₂浓度: ${s.oxygen}%<br/>目标生物: ${s.targetStage}<br/>结果: <span style="color:${s.success ? '#556b2f' : '#c0392b'};font-weight:bold;">${s.success ? '成功' : '部分失败'}</span>`;
            }
        },
        grid: { left: 200, right: 30, top: 20, bottom: 50 },
        xAxis: {
            type: 'value',
            min: 0,
            max: 1440,
            axisLabel: {
                formatter: function(v) {
                    return `${String(Math.floor(v / 60)).padStart(2, '0')}:${String(v % 60).padStart(2, '0')}`;
                },
                fontSize: 10
            },
            name: '时间轴（一天）',
            nameLocation: 'middle',
            nameGap: 30,
            nameTextStyle: { fontSize: 11 }
        },
        yAxis: {
            type: 'category',
            data: categoryData,
            axisLabel: { fontSize: 11 }
        },
        series: [{
            type: 'custom',
            renderItem: function(params, api) {
                const categoryIndex = api.value(0);
                const start = api.coord([api.value(1), categoryIndex]);
                const end = api.coord([api.value(2), categoryIndex]);
                const height = api.size([0, 1])[1] * 0.6;
                const rectShape = echarts.graphic.clipRectByRect({
                    x: start[0],
                    y: start[1] - height / 2,
                    width: end[0] - start[0],
                    height: height
                }, {
                    x: params.coordSys.x,
                    y: params.coordSys.y,
                    width: params.coordSys.width,
                    height: params.coordSys.height
                });
                return rectShape && {
                    type: 'rect',
                    transition: ['shape'],
                    shape: rectShape,
                    style: api.style()
                };
            },
            encode: { x: [1, 2], y: 0 },
            data: barData
        }]
    });
};

window.renderFiberDamageChart = function(domId, comparisons) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    chart.setOption({
        tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
        legend: { data: ['纤维损失率(%)', '色差ΔE'], top: 0, textStyle: { fontSize: 11 } },
        grid: { left: 55, right: 55, top: 40, bottom: 50 },
        xAxis: {
            type: 'category',
            data: comparisons.map(c => c.label),
            axisLabel: { rotate: 25, fontSize: 10 }
        },
        yAxis: [
            {
                type: 'value',
                name: '纤维损失率(%)',
                min: 0,
                max: 10,
                axisLabel: { color: '#8e2f3f', fontSize: 10 },
                nameTextStyle: { color: '#8e2f3f', fontSize: 11 }
            },
            {
                type: 'value',
                name: '色差ΔE',
                min: 0,
                max: 8,
                axisLabel: { color: '#a0522d', fontSize: 10 },
                nameTextStyle: { color: '#a0522d', fontSize: 11 }
            }
        ],
        series: [
            {
                name: '纤维损失率(%)',
                type: 'bar',
                data: comparisons.map(c => +c.loss.toFixed(2)),
                itemStyle: {
                    color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                        { offset: 0, color: '#8e2f3fcc' },
                        { offset: 1, color: '#8e2f3f66' }
                    ]),
                    borderRadius: [4, 4, 0, 0]
                },
                barWidth: '35%'
            },
            {
                name: '色差ΔE',
                type: 'bar',
                yAxisIndex: 1,
                data: comparisons.map(c => +c.deltaE.toFixed(2)),
                itemStyle: {
                    color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                        { offset: 0, color: '#a0522dcc' },
                        { offset: 1, color: '#a0522d66' }
                    ]),
                    borderRadius: [4, 4, 0, 0]
                },
                barWidth: '35%'
            }
        ]
    });
};

window.buildTreatmentFormPanel = function(containerId) {
    const c = document.getElementById(containerId);
    if (!c) return;
    const textiles = window.mockTextileList ? window.mockTextileList() : (function() {
        const arr = [];
        const mats = ['桑蚕丝缎', '云锦', '蜀锦', '宋锦', '缂丝', '妆花缎', '花绫', '刺绣'];
        for (let i = 1; i <= 100; i++) {
            arr.push({ id: i, name: `${['云龙', '团花', '缠枝', '花鸟', '龙凤', '麒麟'][i % 6]}纹${mats[i % 8]}${String(i).padStart(3, '0')}` });
        }
        return arr;
    })();
    c.innerHTML = `
        <div class="form-group">
            <label class="form-label">选择织绣品</label>
            <select id="tf-textile" class="form-control">
                ${textiles.map(t => `<option value="${t.id}">TX-${String(t.id).padStart(4, '0')} ${t.name}</option>`).join('')}
            </select>
        </div>
        <div class="form-group">
            <label class="form-label">目标生物</label>
            <select id="tf-stage" class="form-control">
                <option value="虫卵">虫卵</option>
                <option value="幼虫" selected>幼虫</option>
                <option value="成虫">成虫</option>
                <option value="真菌">真菌</option>
                <option value="全部">全部（依次处理）</option>
            </select>
        </div>
        <div class="form-group">
            <label class="form-label">
                目标氧浓度: <span id="tf-oxygen-val" class="form-value-badge">1.0%</span>
            </label>
            <input type="range" id="tf-oxygen" class="form-control-slider-range" min="0.1" max="5" step="0.1" value="1">
            <div class="slider-marks"><span>0.1%</span><span>2.5%</span><span>5%</span></div>
        </div>
        <div class="form-group">
            <label class="form-label">
                氮气流量: <span id="tf-flow-val" class="form-value-badge">12 L/min</span>
            </label>
            <input type="range" id="tf-flow" class="form-control-slider-range" min="5" max="20" step="0.5" value="12">
            <div class="slider-marks"><span>5</span><span>12.5</span><span>20</span></div>
        </div>
        <div class="form-group">
            <label class="form-label">
                暴露时间: <span id="tf-time-val" class="form-value-badge">720 min</span>
            </label>
            <input type="range" id="tf-time" class="form-control-slider-range" min="30" max="1440" step="15" value="720">
            <div class="slider-marks"><span>0.5h</span><span>12h</span><span>24h</span></div>
        </div>
        <div class="form-group">
            <label class="form-label">
                温度: <span id="tf-temp-val" class="form-value-badge">25°C</span>
            </label>
            <input type="range" id="tf-temp" class="form-control-slider-range" min="15" max="35" step="0.5" value="25">
            <div class="slider-marks"><span>15</span><span>25</span><span>35</span></div>
        </div>
        <div class="form-group">
            <label class="form-label">
                湿度: <span id="tf-hum-val" class="form-value-badge">50%</span>
            </label>
            <input type="range" id="tf-hum" class="form-control-slider-range" min="30" max="70" step="1" value="50">
            <div class="slider-marks"><span>30</span><span>50</span><span>70</span></div>
        </div>
        <div class="form-result-preview" id="tf-preview">
            <div class="preview-title">📋 模拟参数预览</div>
            <div class="preview-grid">
                <div><span class="preview-label">预计死亡率</span><span id="pr-mortality" class="preview-val danger">-</span></div>
                <div><span class="preview-label">预计纤维损失</span><span id="pr-loss" class="preview-val warning">-</span></div>
                <div><span class="preview-label">预计色差ΔE</span><span id="pr-deltae" class="preview-val info">-</span></div>
                <div><span class="preview-label">氮气消耗</span><span id="pr-n2" class="preview-val">-</span></div>
            </div>
        </div>
        <button class="btn-primary treatment-submit-btn" onclick="window.submitNitrogenTreatment()">
            🚀 开始模拟治疗
        </button>
    `;
    const sliderBind = [
        ['tf-oxygen', 'tf-oxygen-val', v => v + '%'],
        ['tf-flow', 'tf-flow-val', v => v + ' L/min'],
        ['tf-time', 'tf-time-val', v => v + ' min'],
        ['tf-temp', 'tf-temp-val', v => v + '°C'],
        ['tf-hum', 'tf-hum-val', v => v + '%']
    ];
    sliderBind.forEach(([id, vid, fmt]) => {
        const el = document.getElementById(id);
        const vel = document.getElementById(vid);
        if (!el) return;
        const update = () => {
            if (vel) vel.textContent = fmt(el.value);
            window.updateTreatmentPreview();
        };
        el.addEventListener('input', update);
    });
    window.updateTreatmentPreview();
};

window.updateTreatmentPreview = function() {
    const stage = document.getElementById('tf-stage')?.value || '幼虫';
    const oxygen = parseFloat(document.getElementById('tf-oxygen')?.value || 1);
    const time = parseFloat(document.getElementById('tf-time')?.value || 720);
    const temp = parseFloat(document.getElementById('tf-temp')?.value || 25);
    const hum = parseFloat(document.getElementById('tf-hum')?.value || 50);
    const flow = parseFloat(document.getElementById('tf-flow')?.value || 12);
    const stageIdx = Math.max(0, window.NITROGEN_STAGES.indexOf(stage));
    const tempFactor = 1 + (temp - 25) * 0.02;
    const humFactor = 1 + (hum - 50) * 0.005;
    const m = Math.min(99.9, window.calculateMortality(oxygen, time, stageIdx) * tempFactor * humFactor);
    const loss = Math.max(0.1, (time / 1440) * (1 + (35 - oxygen) / 5) * (0.3 + tempFactor * 0.2));
    const deltaE = Math.max(0.2, (time / 1440) * (1 + (35 - oxygen) / 8) * (0.4 + tempFactor * 0.15));
    const n2 = (flow * time / 60).toFixed(1);
    const mEl = document.getElementById('pr-mortality');
    const lEl = document.getElementById('pr-loss');
    const dEl = document.getElementById('pr-deltae');
    const nEl = document.getElementById('pr-n2');
    if (mEl) mEl.textContent = m.toFixed(1) + '%';
    if (lEl) lEl.textContent = loss.toFixed(2) + '%';
    if (dEl) dEl.textContent = deltaE.toFixed(2);
    if (nEl) nEl.textContent = n2 + ' m³';
    return { mortality: m, loss, deltaE, n2, oxygen, time, stage, flow, temp, hum };
};

window.submitNitrogenTreatment = async function() {
    const params = window.updateTreatmentPreview();
    const textileId = document.getElementById('tf-textile')?.value || 1;
    const payload = {
        textileId: parseInt(textileId),
        targetStage: params.stage,
        oxygenPercent: params.oxygen,
        nitrogenFlow: params.flow,
        exposureMinutes: params.time,
        temperature: params.temp,
        humidity: params.hum
    };
    const stageIdx = Math.max(0, window.NITROGEN_STAGES.indexOf(params.stage));
    try {
        const res = await fetch('/api/treatment-vulnerability/nitrogen/submit', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        }).then(r => r.json()).catch(() => null);
        const mortality = res?.estimatedMortality ?? params.mortality;
        window.renderProbitCurve('probitCurveChart', params.stage, params.time, params.oxygen, mortality);
        window.toast(`模拟完成！预计${params.stage}死亡率: ${mortality.toFixed(1)}%`, mortality >= 95 ? 'success' : 'warning');
    } catch (e) {
        window.renderProbitCurve('probitCurveChart', params.stage, params.time, params.oxygen, params.mortality);
        window.toast('模拟完成（使用本地模型）', 'info');
    }
};

window.generateNitrogenMockData = function() {
    const oxygenRange = [];
    for (let o = 0.1; o <= 5.05; o += 0.2) oxygenRange.push(+o.toFixed(1));
    const timeRange = [];
    for (let t = 30; t <= 1445; t += 45) timeRange.push(t);
    const sessions = [];
    const names = ['云龙纹云锦023', '缠枝莲花绫045', '龙凤妆花缎067', '麒麟纹宋锦089', '牡丹缂丝012'];
    for (let i = 0; i < 8; i++) {
        const start = 60 * (3 + i * 2.5);
        const dur = 300 + Math.floor(Math.random() * 600);
        const d = new Date(); d.setDate(d.getDate() - (i + 1) * 7);
        sessions.push({
            textileName: names[i % names.length] + ` #${i + 1}`,
            startIdx: Math.floor(start),
            endIdx: Math.floor(start + dur),
            dateStr: `${d.getMonth() + 1}/${d.getDate()}`,
            oxygen: +(0.5 + Math.random() * 2).toFixed(1),
            targetStage: window.NITROGEN_STAGES[i % 4],
            success: Math.random() > 0.2
        });
    }
    const comparisons = [
        { label: '0.5% O₂ / 6h', loss: 0.8, deltaE: 0.9 },
        { label: '0.5% O₂ / 12h', loss: 1.5, deltaE: 1.4 },
        { label: '0.5% O₂ / 24h', loss: 2.8, deltaE: 2.3 },
        { label: '1.0% O₂ / 6h', loss: 0.5, deltaE: 0.6 },
        { label: '1.0% O₂ / 12h', loss: 1.1, deltaE: 1.1 },
        { label: '1.0% O₂ / 24h', loss: 2.1, deltaE: 1.9 },
        { label: '2.0% O₂ / 12h', loss: 0.7, deltaE: 0.8 },
        { label: '2.0% O₂ / 24h', loss: 1.4, deltaE: 1.5 }
    ];
    return { oxygenRange, timeRange, sessions, comparisons };
};

window.initNitrogenTreatmentView = function() {
    const data = window.generateNitrogenMockData();
    window.buildTreatmentFormPanel('treatmentFormPanel');
    window.NITROGEN_STAGES.forEach((_, idx) => {
        window.renderMortalityHeatmap(window.HEATMAP_DOMS[idx], data.oxygenRange, data.timeRange, idx);
    });
    window.renderProbitCurve('probitCurveChart', '幼虫', 720, 1.0, 94.5);
    window.renderTreatmentTimeline('treatmentTimelineChart', data.sessions);
    window.renderFiberDamageChart('fiberDamageChart', data.comparisons);
};
