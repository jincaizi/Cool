using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Network
{
    /// <summary>
    /// 客户端预测与服务端校验
    /// </summary>
    public class NetworkPrediction
    {
        // 预测位置队列（key = sequence）
        private readonly SortedList<uint, PredictedFrame> _predictedFrames = new SortedList<uint, PredictedFrame>();

        // 偏差阈值（超过则 rubber-band）
        private const float POSITION_DEVIATION_THRESHOLD = 0.5f;
        private const float ROTATION_DEVIATION_THRESHOLD = 5f;
        private const int MAX_PREDICTED_FRAMES = 60;

        private uint _lastServerSequence;

        private struct PredictedFrame
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public uint Sequence;
        }

        /// <summary>
        /// 记录预测帧
        /// </summary>
        public void RecordPredictedFrame(uint sequence, Vector3 position, Quaternion rotation)
        {
            // 清理过期帧
            if (_predictedFrames.Count > MAX_PREDICTED_FRAMES)
            {
                _predictedFrames.RemoveAt(0);
            }

            _predictedFrames[sequence] = new PredictedFrame
            {
                Position = position,
                Rotation = rotation,
                Sequence = sequence
            };
        }

        /// <summary>
        /// 处理服务端确认/拒绝
        /// </summary>
        public bool ValidateAndCorrect(uint serverSequence, Vector3 serverPosition, Quaternion serverRotation,
            out Vector3 correctedPosition, out Quaternion correctedRotation)
        {
            correctedPosition = serverPosition;
            correctedRotation = serverRotation;

            // 跳过已确认的旧帧
            if (serverSequence <= _lastServerSequence)
                return false;

            _lastServerSequence = serverSequence;

            // 检查是否有对应的预测帧
            if (_predictedFrames.TryGetValue(serverSequence, out var predictedFrame))
            {
                float posDeviation = Vector3.Distance(predictedFrame.Position, serverPosition);
                float rotDeviation = Quaternion.Angle(predictedFrame.Rotation, serverRotation);

                if (posDeviation > POSITION_DEVIATION_THRESHOLD || rotDeviation > ROTATION_DEVIATION_THRESHOLD)
                {
                    return true; // 表示做了校正
                }
            }

            return false;
        }

        /// <summary>
        /// 获取下一个序列号
        /// </summary>
        public uint GetNextSequence()
        {
            return _lastServerSequence + 1;
        }
    }
}
