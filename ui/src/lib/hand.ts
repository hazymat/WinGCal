// Hand-drawn marks for the Paper skin: wobbly circles and arrows, seeded so each day (and each event) keeps its own shape
export function seededRandom(seedText: string) {
  let state = 2166136261
  for (const character of seedText) state = Math.imul(state ^ character.charCodeAt(0), 16777619)
  return () => { state = Math.imul(state ^ (state >>> 15), 2246822507); state = Math.imul(state ^ (state >>> 13), 3266489909); state ^= state >>> 16; return (state >>> 0) / 4294967296 }
}
const point = (values: number[]) => values.map(value => value.toFixed(1)).join(' ')
// A smooth line through the points (Catmull-Rom as cubic Béziers)
function smoothPath(points: [number, number][]) {
  let path = `M${point(points[0])}`
  for (let index = 0; index < points.length - 1; index++) {
    const before = points[Math.max(0, index - 1)], start = points[index], end = points[index + 1], after = points[Math.min(points.length - 1, index + 2)]
    path += ` C${point([start[0] + (end[0] - before[0]) / 6, start[1] + (end[1] - before[1]) / 6])} ${point([end[0] - (after[0] - start[0]) / 6, end[1] - (after[1] - start[1]) / 6])} ${point(end)}`
  }
  return path
}
// A circle drawn round something in one stroke: a bit off-centre, never quite round, the end overshooting the start (viewBox 0 0 100 100)
export function handCircle(seedText: string) {
  const random = seededRandom(seedText)
  const startAngle = random() * Math.PI * 2, sweep = Math.PI * 2 + 0.35 + random() * 0.55, steps = 11
  const centreX = 50 + (random() - 0.5) * 6, centreY = 50 + (random() - 0.5) * 6, radiusX = 42 + random() * 5, radiusY = 38 + random() * 6, tilt = (random() - 0.5) * 0.5
  const points: [number, number][] = []
  for (let step = 0; step <= steps; step++) {
    const angle = startAngle + sweep * step / steps, drift = 1 + (random() - 0.5) * 0.09 + step * (random() * 0.012) // the pen drifts outward as it goes round
    const x = Math.cos(angle) * radiusX * drift, y = Math.sin(angle) * radiusY * drift
    points.push([centreX + x * Math.cos(tilt) - y * Math.sin(tilt), centreY + x * Math.sin(tilt) + y * Math.cos(tilt)])
  }
  return smoothPath(points)
}
// A slightly wavering line across the width (viewBox 0 0 100 10, stretched to fit)
export function handLine(seedText: string) {
  const random = seededRandom(seedText)
  const points: [number, number][] = []
  const steps = 5
  for (let step = 0; step <= steps; step++) { const inner = step > 0 && step < steps; points.push([step * 100 / steps + (inner ? (random() - 0.5) * 6 : 0), inner ? 5 + (random() - 0.5) * 4.5 : 5]) } // ends level, so a line carried across days joins up
  return smoothPath(points)
}
// An arrowhead drawn in two quick strokes (viewBox 0 0 12 12, tip at the right middle)
export function handHead(seedText: string) {
  const random = seededRandom(seedText + 'head')
  const tipY = 6 + (random() - 0.5) * 1.5
  return `M${point([2 + random() * 2, 1 + random() * 2])} Q${point([7, tipY - 2.5 + random()])} ${point([11, tipY])} M${point([11, tipY])} Q${point([7, tipY + 2.5 - random()])} ${point([2.5 + random() * 2, 10 + random() * 1.5])}`
}
