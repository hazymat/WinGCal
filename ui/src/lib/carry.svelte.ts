// Drag and drop of events (SPEC.md › Moving events): press and hold lifts the event, or every picked event, which then follows the pointer to another day.
// App runs it; views read it to open room for the drop (Agenda's empty days and slot, Week's dashed time box)
export const carry = $state({
  active: false, ids: [] as string[],
  originDay: '', targetDay: '', // dayKey of where they came from and where they'd land
  minutes: 0, previewTop: -1, previewLength: 0, // Week's hours: the time shift, and the dashed box for the first carried event (minutes into the day; -1 = none)
  x: 0, y: 0,
})
