
export default class Helper2 {
  static sortByTime(arr) {
    const parseTime = (timeStr) => {
      if (!timeStr || typeof timeStr !== 'string') return Infinity;

      const [minPart, secPart] = timeStr.split(':');
      const minutes = parseInt(minPart);
      const seconds = parseFloat(secPart.replace(',', '.'));
      return isNaN(minutes) || isNaN(seconds) ? Infinity : minutes * 60 + seconds;
    };

    return [...arr].sort((a, b) => parseTime(a.time) - parseTime(b.time));
  }

  static getDolphinDataByName(data, name) {
    return data.filter(item => item.first_name === name);
  }

  static getDolphinDataGroupedByDate(data) {
    const grouped = data.reduce((acc, item) => {
      if (!acc[item.date]) {
        acc[item.date] = [];
      }
      acc[item.date].push(item);
      return acc;
    }, {});

    Object.keys(grouped).forEach(date => {
      grouped[date] = this.sortByTime(grouped[date]);
    });

    return grouped;
  }
}