
window.PEST_SPECIES = ['衣鱼', '毛衣鱼', '黑毛皮蠹', '衣蛾', '标本皮蠹'];
window.PEST_COLORS = ['#c0392b', '#a0522d', '#2c3e50', '#556b2f', '#8e2f3f'];

window.initPestChart = function(domId) {
    const el = document.getElementById(domId);
    if (!el || !window.echarts) return null;
    if (window.chartInstances && window.chartInstances[domId]) {
        window.chartInstances[domId].dispose();
    }
    const chart = echarts.init(el);
    if (!window.chartInstances) window.chartInstances = {};
    window.chartInstances[domId] = chart;
    window.addEventListener('resize', () => chart.resize());
    return chart;
};

window.renderPestSpeciesBarChart = function(domId, data) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    const dynasties = data.dynasties;
    const series = window.PEST_SPECIES.map((name, i) => ({
        name: name,
        type: 'bar',
        stack: 'total',
        emphasis: { focus: 'series' },
        itemStyle: { color: window.PEST_COLORS[i] },
        data: data.speciesData[i]
    }));
    chart.setOption({
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        legend: { data: window.PEST_SPECIES, top: 0, textStyle: { fontSize: 12 } },
        grid: { left: 50, right: 20, top: 40, bottom: 50 },
        xAxis: { type: 'category', data: dynasties, axisLabel: { rotate: 20, fontSize: 11 } },
        yAxis: { type: 'value', name: '检出数量', nameTextStyle: { fontSize: 11 } },
        series: series
    });
};

window.renderPestProbabilityRadar = function(domId, probabilities) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    chart.setOption({
        tooltip: { trigger: 'item' },
        legend: { data: ['当前织绣', '馆藏平均'], bottom: 0, textStyle: { fontSize: 11 } },
        radar: {
            indicator: window.PEST_SPECIES.map(name => ({ name: name, max: 100 })),
            radius: '65%',
            axisName: { color: '#333', fontSize: 12 },
            splitArea: { areaStyle: { color: ['rgba(192,57,43,0.02)', 'rgba(160,82,45,0.04)'] } }
        },
        series: [{
            type: 'radar',
            data: [
                {
                    value: probabilities.current,
                    name: '当前织绣',
                    itemStyle: { color: '#c0392b' },
                    lineStyle: { color: '#c0392b', width: 2.5 },
                    areaStyle: { color: 'rgba(192,57,43,0.25)' }
                },
                {
                    value: probabilities.avg,
                    name: '馆藏平均',
                    itemStyle: { color: '#1e6091' },
                    lineStyle: { color: '#1e6091', width: 2, type: 'dashed' },
                    areaStyle: { color: 'rgba(30,96,145,0.12)' }
                }
            ]
        }]
    });
};

window.renderInstarsHistogram = function(domId, data) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    const stages = ['卵期', '1龄', '2龄', '3龄', '4龄', '5龄', '蛹期', '成虫期'];
    const colors = ['#556b2f', '#7cb342', '#f39c12', '#e67e22', '#d35400', '#a0522d', '#8e2f3f', '#c0392b'];
    chart.setOption({
        tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
        legend: { data: window.PEST_SPECIES, top: 0, textStyle: { fontSize: 11 } },
        grid: { left: 50, right: 20, top: 40, bottom: 40 },
        xAxis: { type: 'category', data: stages, axisLabel: { fontSize: 11 } },
        yAxis: { type: 'value', name: '个体数', nameTextStyle: { fontSize: 11 } },
        series: window.PEST_SPECIES.map((name, i) => ({
            name: name,
            type: 'bar',
            barGap: '10%',
            itemStyle: { color: window.PEST_COLORS[i], borderRadius: [4, 4, 0, 0] },
            emphasis: { focus: 'series' },
            data: data[i]
        }))
    });
};

window.renderPestTrendLine = function(domId, timeSeries) {
    const chart = window.initPestChart(domId);
    if (!chart) return;
    chart.setOption({
        tooltip: { trigger: 'axis' },
        legend: { data: window.PEST_SPECIES, top: 0, textStyle: { fontSize: 11 } },
        grid: { left: 50, right: 20, top: 40, bottom: 60 },
        xAxis: { type: 'category', boundaryGap: false, data: timeSeries.dates, axisLabel: { rotate: 30, fontSize: 10, interval: Math.floor(timeSeries.dates.length / 10) } },
        yAxis: { type: 'value', name: '虫口密度指数', nameTextStyle: { fontSize: 11 } },
        dataZoom: [{ type: 'inside' }, { type: 'slider', height: 18, bottom: 5 }],
        series: window.PEST_SPECIES.map((name, i) => ({
            name: name,
            type: 'line',
            stack: 'Total',
            smooth: true,
            symbol: 'none',
            lineStyle: { width: 1, color: window.PEST_COLORS[i] },
            itemStyle: { color: window.PEST_COLORS[i] },
            areaStyle: {
                color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                    { offset: 0, color: window.PEST_COLORS[i] + 'cc' },
                    { offset: 1, color: window.PEST_COLORS[i] + '10' }
                ])
            },
            emphasis: { focus: 'series' },
            data: timeSeries.values[i]
        }))
    });
};

window.renderPestDetailTable = function(data) {
    const tbody = document.querySelector('#pestDetailTable tbody');
    if (!tbody) return;
    tbody.innerHTML = '';
    data.forEach((row, idx) => {
        const rl = row.riskLevel;
        const rc = rl >= 4 ? 'critical' : rl >= 3 ? 'high' : rl >= 2 ? 'medium' : 'low';
        const rn = { critical: '严重', high: '高', medium: '中', low: '低' }[rc];
        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td><span class="rank-badge ${idx < 3 ? 'rank-' + (idx + 1) : 'rank-other'}">${idx + 1}</span> TX-${String(row.id).padStart(4, '0')}</td>
            <td><strong>${escapeHtml(row.name)}</strong></td>
            <td>${escapeHtml(row.dynasty)}</td>
            <td><span style="display:inline-block;padding:3px 10px;border-radius:10px;background:${window.PEST_COLORS[row.dominantIdx]}18;color:${window.PEST_COLORS[row.dominantIdx]};font-weight:600;font-size:12px;">${window.PEST_SPECIES[row.dominantIdx]}</span></td>
            <td style="font-family:Consolas;font-weight:600;">${(row.confidence * 100).toFixed(1)}%</td>
            <td><small>卵${row.instars[0]}·幼${row.instars.slice(1, 6).reduce((a, b) => a + b, 0)}·蛹${row.instars[6]}·成${row.instars[7]}</small></td>
            <td style="font-family:Consolas;color:#c0392b;font-weight:600;">${row.density.toFixed(2)}</td>
            <td><span class="risk-badge ${rc}">${rn}</span></td>`;
        tbody.appendChild(tr);
    });
};

window.generatePestMockData = function() {
    const dynasties = ['明代早期', '明代中期', '明代晚期', '清代早期', '清代中期', '清代晚期'];
    const speciesData = window.PEST_SPECIES.map(() => dynasties.map(() => Math.floor(5 + Math.random() * 40)));
    const probabilities = {
        current: [85, 42, 68, 35, 52],
        avg: [45, 38, 52, 40, 33]
    };
    const instarsData = window.PEST_SPECIES.map(() => {
        const base = Array(8).fill(0).map(() => Math.floor(5 + Math.random() * 50));
        const midIdx = [1, 2, 3, 4];
        midIdx.forEach(i => base[i] = Math.floor(base[i] * (1.2 + Math.random() * 0.8)));
        return base;
    });
    const dates = [];
    for (let i = 29; i >= 0; i--) {
        const d = new Date(); d.setDate(d.getDate() - i);
        dates.push(`${d.getMonth() + 1}/${d.getDate()}`);
    }
    const trendValues = window.PEST_SPECIES.map((_, idx) => {
        let base = 10 + idx * 5;
        return dates.map((_, i) => {
            base += (Math.random() - 0.4) * 3;
            base = Math.max(2, Math.min(base, 60));
            return +base.toFixed(1);
        });
    });
    const dynastiesList = ['明代早期', '明代中期', '明代晚期', '清代早期', '清代中期', '清代晚期'];
    const materials = ['桑蚕丝缎', '云锦', '蜀锦', '宋锦', '缂丝', '妆花缎', '花绫', '刺绣'];
    const tableData = [];
    for (let i = 1; i <= 20; i++) {
        const probs = Array(5).fill(0).map(() => Math.random());
        const sum = probs.reduce((a, b) => a + b, 0);
        const norm = probs.map(p => p / sum);
        const dominantIdx = norm.indexOf(Math.max(...norm));
        const instars = Array(8).fill(0).map(() => Math.floor(2 + Math.random() * 30));
        tableData.push({
            id: i,
            name: `${['云龙', '团花', '缠枝', '花鸟', '龙凤', '麒麟'][i % 6]}纹${materials[i % 8]}${String(i).padStart(3, '0')}`,
            dynasty: dynastiesList[i % 6],
            dominantIdx: dominantIdx,
            confidence: 0.75 + Math.random() * 0.23,
            instars: instars,
            density: +(5 + Math.random() * 40).toFixed(2),
            riskLevel: 1 + Math.floor(Math.random() * 4)
        });
    }
    tableData.sort((a, b) => b.density - a.density);
    return {
        bar: { dynasties, speciesData },
        radar: probabilities,
        histogram: instarsData,
        trend: { dates, values: trendValues },
        table: tableData
    };
};

window.initPestClassificationView = function() {
    const data = window.generatePestMockData();
    window.renderPestSpeciesBarChart('pestSpeciesBarChart', data.bar);
    window.renderPestProbabilityRadar('pestProbabilityRadar', data.radar);
    window.renderInstarsHistogram('instarsHistogram', data.histogram);
    window.renderPestTrendLine('pestTrendLine', data.trend);
    window.renderPestDetailTable(data.table);
};

if (typeof escapeHtml !== 'function') {
    window.escapeHtml = function(s) {
        if (s == null) return '';
        return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    };
}
