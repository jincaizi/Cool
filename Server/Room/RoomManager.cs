using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace KcpServer
{
    public class RoomManagerConfig
    {
        public TimeSpan RoomTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public int MaxRooms { get; set; } = 1000;
    }

    public sealed class RoomManager
    {
        private readonly RoomManagerConfig _config;
        private readonly ConcurrentDictionary<string, Room> _rooms = new ConcurrentDictionary<string, Room>();
        private readonly ConcurrentDictionary<long, string> _playerRoomMap = new ConcurrentDictionary<long, string>();

        public int TotalPlayerCount => _playerRoomMap.Count;
        public int RoomCount => _rooms.Count;

        public RoomManager(RoomManagerConfig config)
        {
            _config = config;
        }

        public Room GetOrCreateRoom(string roomId)
        {
            if (_rooms.TryGetValue(roomId, out var existingRoom))
                return existingRoom;

            var newRoom = new Room(roomId);
            if (!_rooms.TryAdd(roomId, newRoom))
            {
                return _rooms[roomId];
            }
            return newRoom;
        }

        public bool TryGetRoom(string roomId, out Room room)
        {
            return _rooms.TryGetValue(roomId, out room);
        }

        public bool DestroyRoom(string roomId)
        {
            return _rooms.TryRemove(roomId, out _);
        }

        public IReadOnlyList<Room> GetAllRooms()
        {
            return _rooms.Values.ToList();
        }

        public Room? GetPlayerRoom(long playerId)
        {
            if (_playerRoomMap.TryGetValue(playerId, out var roomId))
            {
                if (_rooms.TryGetValue(roomId, out var room))
                    return room;
            }
            return null;
        }

        public bool TryAddPlayerToRoom(long playerId, KcpServerSession session, PlayerState state, string roomId)
        {
            if (_playerRoomMap.TryGetValue(playerId, out var existingRoomId))
            {
                if (existingRoomId == roomId)
                    return true;
                TryRemovePlayer(playerId);
            }

            var room = GetOrCreateRoom(roomId);
            if (!room.AddPlayer(playerId, session, state))
                return false;

            state.ZoneId = roomId;
            _playerRoomMap[playerId] = roomId;
            return true;
        }

        public bool TryRemovePlayer(long playerId)
        {
            if (!_playerRoomMap.TryRemove(playerId, out var roomId))
                return false;

            if (_rooms.TryGetValue(roomId, out var room))
            {
                room.RemovePlayer(playerId);
            }
            return true;
        }

        public bool TryChangePlayerZone(long playerId, KcpServerSession session, PlayerState state, string newZoneId)
        {
            if (!_playerRoomMap.TryGetValue(playerId, out var oldZoneId))
                return false;

            if (_rooms.TryGetValue(oldZoneId, out var oldRoom))
            {
                oldRoom.RemovePlayer(playerId);
            }

            var newRoom = GetOrCreateRoom(newZoneId);
            if (!newRoom.AddPlayer(playerId, session, state))
                return false;

            state.ZoneId = newZoneId;
            _playerRoomMap[playerId] = newZoneId;
            return true;
        }

        public void CleanupEmptyRooms()
        {
            foreach (var room in _rooms.Values.ToList())
            {
                if (room.PlayerCount == 0)
                {
                    DestroyRoom(room.RoomId);
                }
            }
        }

        public IEnumerable<long> GetPlayerIdsInRoom(string roomId)
        {
            if (_rooms.TryGetValue(roomId, out var room))
            {
                return room.GetAllPlayerStates().Keys.ToList();
            }
            return Enumerable.Empty<long>();
        }
    }
}
