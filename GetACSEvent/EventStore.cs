using System;
using System.Collections.Generic;

namespace GetACSEvent
{
    public class AcsEvent
    {
        public DateTime TimeUtc { get; set; }
        public string DeviceIP { get; set; }
        public string DeviceName { get; set; }
        public string DeviceID { get; set; }
        public string AreaID { get; set; }
        public string Remark { get; set; }
        public string MajorType { get; set; }
        public string MinorType { get; set; }
        public string CardNo { get; set; }
        public string EmployeeNo { get; set; }
        public string PersonName { get; set; }
        public string CardType { get; set; }
        public uint DoorNo { get; set; }
    }

    public class EventStore
    {
        private readonly object _sync = new object();
        private readonly List<AcsEvent> _events = new List<AcsEvent>();
        private readonly int _capacity;

        public EventStore(int capacity = 1000)
        {
            _capacity = capacity > 0 ? capacity : 1000;
        }

        public void Add(AcsEvent e)
        {
            if (e == null) return;
            lock (_sync)
            {
                _events.Add(e);
                if (_events.Count > _capacity)
                {
                    int remove = _events.Count - _capacity;
                    _events.RemoveRange(0, remove);
                }
            }
        }

        public List<AcsEvent> Snapshot()
        {
            lock (_sync)
            {
                return new List<AcsEvent>(_events);
            }
        }
    }
}

