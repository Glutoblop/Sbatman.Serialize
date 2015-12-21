#region Usings

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#endregion Usings

namespace Sbatman.Serialize
{
    /// <summary>
    ///     The Packet class is a light class that is used for serialising and deserialising data.
    /// </summary>
    public class Packet : IDisposable
    {
        /// <summary>
        ///     This is the initial size of the internal byte array size of the packet
        /// </summary>
        private const Int32 INITAL_DATA_SIZE = 128;

        /// <summary>
        ///     This 4 byte sequence is used to improve start of packet regognition, it isnt the sole descriptor of the packet start
        ///     as this would possibly cause issues with packets with byte sequences within them that happened to contains this.
        /// </summary>
        public static readonly Byte[] PacketStart = { 0, 48, 21, 0 };

        /// <summary>
        ///     The type id of the packet
        /// </summary>
        public readonly UInt16 Type;

        /// <summary>
        ///     The internal data array of the packet
        /// </summary>
        protected Byte[] _Data;

        /// <summary>
        ///     The current position in the internal data array
        /// </summary>
        protected UInt32 _DataPos;

        /// <summary>
        ///     Whether the packet is disposed
        /// </summary>
        protected Boolean _Disposed;

        /// <summary>
        ///     A copy of all the objects packed in this packet
        /// </summary>
        protected List<Object> _PacketObjects;

        /// <summary>
        ///     The number of paramerters that are stored in the packet
        /// </summary>
        protected UInt16 _ParamCount;

        /// <summary>
        ///     A temp copy of the byte array generated by this packet, this is used as a cache for packets with multiple targets, this will be cleared by a number of interactions with the packet
        /// </summary>
        protected Byte[] _ReturnByteArray;

        private static void GetBytes(ParamTypes t, Object obj, Byte[] data, Int32 datpos)
        {
            t = (ParamTypes)((UInt32)t & ~128);
            switch (t)
            {
                case ParamTypes.FLOAT: BitConverter.GetBytes((Single)obj).CopyTo(data, datpos); break;
                case ParamTypes.DOUBLE: BitConverter.GetBytes((Double)obj).CopyTo(data, datpos); break;
                case ParamTypes.INT16: BitConverter.GetBytes((Int16)obj).CopyTo(data, datpos); break;
                case ParamTypes.UINT16: BitConverter.GetBytes((UInt16)obj).CopyTo(data, datpos); break;
                case ParamTypes.INT32: BitConverter.GetBytes((Int32)obj).CopyTo(data, datpos); break;
                case ParamTypes.UINT32: BitConverter.GetBytes((UInt32)obj).CopyTo(data, datpos); break;
                case ParamTypes.INT64: BitConverter.GetBytes((Int64)obj).CopyTo(data, datpos); break;
                case ParamTypes.UINT64: BitConverter.GetBytes((UInt64)obj).CopyTo(data, datpos); break;
                case ParamTypes.BOOL: BitConverter.GetBytes((Boolean)obj).CopyTo(data, datpos); break;
                case ParamTypes.BYTE_PACKET: ((Byte[])obj).CopyTo(data, datpos); break;
                case ParamTypes.UTF8_STRING: Encoding.UTF8.GetBytes((String)obj).CopyTo(data, datpos); break;
                case ParamTypes.COMPRESSED_BYTE_PACKET: ((Byte[])obj).CopyTo(data, datpos); break;
                case ParamTypes.DECIMAL:
                    {
                        Int32[] sections = Decimal.GetBits((Decimal)obj);
                        for (Int32 i = 0; i < 4; i++) BitConverter.GetBytes(sections[i]).CopyTo(data, datpos + (i * 4));
                    }
                    break;

                case ParamTypes.TIMESPAN: BitConverter.GetBytes(((TimeSpan)obj).Ticks).CopyTo(data, datpos); break;
                case ParamTypes.DATETIME: BitConverter.GetBytes(((DateTime)obj).Ticks).CopyTo(data, datpos); break;
                case ParamTypes.GUID: ((Guid)obj).ToByteArray().CopyTo(data, datpos); break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(t), t, null);
            }
        }

        /// <summary>
        ///     Creates a new packet with the specified type id
        /// </summary>
        /// <param name="type">The packets type ID</param>
        /// <param name="internalDataArraySize">The initial size of the packets internal data array, defaults to INITAL_DATA_SIZE </param>
        public Packet(UInt16 type, Int32 internalDataArraySize = INITAL_DATA_SIZE)
        {
            Type = type;
            _Data = new Byte[internalDataArraySize];
        }

        /// <summary>
        ///     Disposes the packet, destroying all internals, buffers and caches, fails silently if the packet is already disposed
        /// </summary>
        public void Dispose()
        {
            _ReturnByteArray = null;
            if (_Disposed) return;
            _Disposed = true;
            if (_PacketObjects != null)
            {
                _PacketObjects.Clear();
                _PacketObjects = null;
            }
            _Data = null;
        }

        /// <summary>
        ///     Creates a deep copy of this packet
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public Packet Copy()
        {
            if (_Disposed) throw new ObjectDisposedException(ToString());
            Packet p = new Packet(Type)
            {
                _Data = new Byte[_Data.Length]
            };
            _Data.CopyTo(p._Data, 0);
            p._DataPos = _DataPos;
            if (_PacketObjects != null) p._PacketObjects = new List<Object>(_PacketObjects);
            p._ParamCount = _ParamCount;
            p._ReturnByteArray = _ReturnByteArray;
            return p;
        }

        /// <summary>
        ///     Adds a byte array to the packet
        /// </summary>
        /// <param name="byteArray">The bytearray to add</param>
        /// <param name="compress">Whether or not to compress the bytearray, potentially saving large quantitys of badwidth at increased cpu cost</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Byte[] byteArray, Boolean compress = false)
        {
            if (compress) byteArray = Compress(byteArray);
            AddInternal(byteArray, compress ? ParamTypes.COMPRESSED_BYTE_PACKET : ParamTypes.BYTE_PACKET, (UInt32)byteArray.Length, true);
        }

        /// <summary>
        ///     Adds a double to the packet
        /// </summary>
        /// <param name="d">The double to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Double d)
        {
            AddInternal(d, ParamTypes.DOUBLE, 8);
        }

        /// <summary>
        ///     Adds a float to the packet
        /// </summary>
        /// <param name="f">The float to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Single f)
        {
            AddInternal(f, ParamTypes.FLOAT, 4);
        }

        /// <summary>
        ///     Adds a boolean to the packet
        /// </summary>
        /// <param name="b">The bool to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Boolean b)
        {
            AddInternal(b, ParamTypes.BOOL, 1);
        }

        /// <summary>
        ///     Adds a long to the packet
        /// </summary>
        /// <param name="l">The long to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Int64 l)
        {
            AddInternal(l, ParamTypes.INT64, 8);
        }

        /// <summary>
        ///     Adds an int32 to the packet
        /// </summary>
        /// <param name="i">The Int32 to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Int32 i)
        {
            AddInternal(i, ParamTypes.INT32, 4);
        }

        /// <summary>
        ///     Adds an int64 to the packet
        /// </summary>
        /// <param name="i">The int64 to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(UInt64 i)
        {
            AddInternal(i, ParamTypes.UINT64, 8);
        }

        /// <summary>
        ///     Adds an Int16 to the packet
        /// </summary>
        /// <param name="i">The int16 to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Int16 i)
        {
            AddInternal(i, ParamTypes.INT16, 2);
        }

        /// <summary>
        ///     Adds an Int16 to the packet
        /// </summary>
        /// <param name="i">The int16 to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(UInt16 i)
        {
            AddInternal(i, ParamTypes.UINT16, 2);
        }

        /// <summary>
        ///     Adds a Uint32 to the packet
        /// </summary>
        /// <param name="u">The uint32 to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(UInt32 u)
        {
            AddInternal(u, ParamTypes.UINT32, 4);
        }

        /// <summary>
        ///     Adds a decimal to the packet
        /// </summary>
        /// <param name="d">The decimal to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Decimal d)
        {
            AddInternal(d, ParamTypes.DECIMAL, 16);
        }

        /// <summary>
        ///     Adds a Guid to the packet
        /// </summary>
        /// <param name="g">The Guid to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Guid g)
        {
            AddInternal(g, ParamTypes.GUID, 16);
        }

        /// <summary>
        ///     Adds a UTF8 String to the packet
        /// </summary>
        /// <param name="s">The String to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(String s)
        {
            AddInternal(s, ParamTypes.UTF8_STRING, (UInt16)Encoding.UTF8.GetByteCount(s), true);
        }

        /// <summary>
        ///     Adds a TimeSpan to the packet
        /// </summary>
        /// <param name="s">The TimeSpan to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(TimeSpan s)
        {
            AddInternal(s, ParamTypes.TIMESPAN, 8);
        }

        /// <summary>
        ///     Adds a DateTime to the packet
        /// </summary>
        /// <param name="s">The DateTime to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(DateTime s)
        {
            AddInternal(s, ParamTypes.DATETIME, 8);
        }

        /// <summary>
        ///     Adds a Packet to the packet, this packet assumes ownership over the provided packet, do not dispose it
        /// </summary>
        /// <param name="p">The Packet to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public void Add(Packet p)
        {
            Byte[] data = p.ToByteArray();
            AddInternal(p, ParamTypes.PACKET, (UInt32)data.Length, true);
        }

        /// <summary>
        /// Adds a list of Doubles to the packet
        /// </summary>
        /// <param name="list">The list of doubles to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        /// <exception cref="ArgumentOutOfRangeException">Will throw if list is Null, empty or has more than UInt16.MaxValue elements</exception>
        public void Add(IReadOnlyCollection<Double> list)
        {
            AddToListInternal(list, ParamTypes.DOUBLE, 8);
        }

        /// <summary>
        /// Adds a list of floats to the packet
        /// </summary>
        /// <param name="list">The list of doubles to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        /// <exception cref="ArgumentOutOfRangeException">Will throw if list is Null, empty or has more than UInt16.MaxValue elements</exception>
        public void Add(IReadOnlyCollection<Single> list)
        {
            AddToListInternal(list, ParamTypes.FLOAT, 4);
        }

        /// <summary>
        /// Adds a list of Int32s to the packet
        /// </summary>
        /// <param name="list">The list of doubles to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        /// <exception cref="ArgumentOutOfRangeException">Will throw if list is Null, empty or has more than UInt16.MaxValue elements</exception>
        public void Add(IReadOnlyCollection<Int32> list)
        {
            AddToListInternal(list, ParamTypes.INT32, 4);
        }
        

        /// <summary>
        /// Adds a list of Int32s to the packet
        /// </summary>
        /// <param name="list">The list of doubles to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        /// <exception cref="ArgumentOutOfRangeException">Will throw if list is Null, empty or has more than UInt16.MaxValue elements</exception>
        public void Add(IReadOnlyCollection<UInt32> list)
        {
            AddToListInternal(list, ParamTypes.UINT32, 4);
        }

        /// <summary>
        /// Adds a list of bool's to the packet
        /// </summary>
        /// <param name="list">The list of doubles to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        /// <exception cref="ArgumentOutOfRangeException">Will throw if list is Null, empty or has more than UInt16.MaxValue elements</exception>
        public void Add(IReadOnlyCollection<Boolean> list)
        {
            AddToListInternal(list, ParamTypes.BOOL, 1);
        }

        /// <summary>
        /// Adds a list of Int64s to the packet
        /// </summary>
        /// <param name="list">The list of doubles to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        /// <exception cref="ArgumentOutOfRangeException">Will throw if list is Null, empty or has more than UInt16.MaxValue elements</exception>
        public void Add(IReadOnlyCollection<Int64> list)
        {
            AddToListInternal(list, ParamTypes.INT64, 8);
        }

        /// <summary>
        /// Adds a list of Decimals to the packet
        /// </summary>
        /// <param name="list">The list of doubles to add</param>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        /// <exception cref="ArgumentOutOfRangeException">Will throw if list is Null, empty or has more than UInt16.MaxValue elements</exception>
        public void Add(IReadOnlyCollection<Decimal> list)
        {
            AddToListInternal(list, ParamTypes.DECIMAL, 16);
        }

        private void AddToListInternal<T>(IReadOnlyCollection<T> list, ParamTypes typeMarker, UInt32 elementSize)
        {
            if (_Disposed) throw new ObjectDisposedException(ToString());
            if (list == null || list.Count == 0 || list.Count > UInt16.MaxValue) throw new ArgumentOutOfRangeException(nameof(list), "Null, empty and > UInt16.MaxValue element lists cannot be added");
            _ReturnByteArray = null;
            UInt32 byteLength = 3 + (elementSize * (UInt32)list.Count);
            while (_DataPos + byteLength >= _Data.Length) ExpandDataArray();
            _Data[_DataPos++] = (Byte)(((Byte)typeMarker) | 128);
            BitConverter.GetBytes((UInt16)list.Count).CopyTo(_Data, (Int32)_DataPos);
            _DataPos += 2;
            foreach (T f in list)
            {
                GetBytes(typeMarker, f, _Data, (Int32)_DataPos);
                _DataPos += elementSize;
            }
            _ParamCount++;
        }

        private void AddInternal<T>(T value, ParamTypes typeMarker, UInt32 elementSize, Boolean specifySize = false)
        {
            if (_Disposed) throw new ObjectDisposedException(ToString());
            _ReturnByteArray = null;
            while (_DataPos + elementSize + (specifySize ? 5 : 1) >= _Data.Length) ExpandDataArray();
            _Data[_DataPos++] = (Byte)(typeMarker);
            if (specifySize)
            {
                BitConverter.GetBytes(elementSize).CopyTo(_Data, (Int32)_DataPos);
                _DataPos += 4;
            }
            GetBytes(typeMarker, value, _Data, (Int32)_DataPos);

            _DataPos += elementSize;
            _ParamCount++;
        }

        /// <summary>
        ///     Converts the back to a bytearray
        /// </summary>
        /// <returns>A byte array representing the packet</returns>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public Byte[] ToByteArray()
        {
            if (_Disposed) throw new ObjectDisposedException(ToString());
            if (_ReturnByteArray != null) return _ReturnByteArray;
            _ReturnByteArray = new Byte[12 + _DataPos];
            PacketStart.CopyTo(_ReturnByteArray, 0);
            BitConverter.GetBytes(_ParamCount).CopyTo(_ReturnByteArray, 4);
            BitConverter.GetBytes(12 + _DataPos).CopyTo(_ReturnByteArray, 6);
            BitConverter.GetBytes(Type).CopyTo(_ReturnByteArray, 10);
            Array.Copy(_Data, 0, _ReturnByteArray, 12, (Int32)_DataPos);
            return _ReturnByteArray;
        }

        /// <summary>
        ///     Returns the list of objects within this packet
        /// </summary>
        /// <returns>An array of the contained objects</returns>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        public Object[] GetObjects()
        {
            if (_Disposed) throw new ObjectDisposedException(ToString());
            return _PacketObjects.ToArray();
        }

        /// <summary>
        ///     Ensures the packet bojects array correctly represents the objects that should be within this packet
        /// </summary>
        /// <exception cref="ObjectDisposedException">Will throw if packet is disposed</exception>
        protected void UpdateObjects()
        {
            if (_Disposed) throw new ObjectDisposedException(ToString());
            if (_PacketObjects != null)
            {
                _PacketObjects.Clear();
                _PacketObjects = null;
            }
            _PacketObjects = new List<Object>(_ParamCount);
            Int32 bytepos = 0;
            for (Int32 x = 0; x < _ParamCount; x++)
            {
                bytepos = (_Data[bytepos] & 128) > 0 ? UnpackList(bytepos) : UnpackValue(bytepos);
            }
        }

        private Int32 UnpackList(Int32 bytepos)
        {
            ParamTypes listType = (ParamTypes) (_Data[bytepos++] & ~128);
            UInt16 listLength = BitConverter.ToUInt16(_Data, bytepos);
            bytepos += 2;
            switch (listType)
            {
                case ParamTypes.DOUBLE:
                    {
                        List<Double> returnList = new List<Double>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            returnList.Add(BitConverter.ToDouble(_Data, bytepos));
                            bytepos += 8;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;

                case ParamTypes.FLOAT:
                    {
                        List<Single> returnList = new List<Single>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            returnList.Add(BitConverter.ToSingle(_Data, bytepos));
                            bytepos += 4;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;

                case ParamTypes.INT32:
                    {
                        List<Int32> returnList = new List<Int32>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            returnList.Add(BitConverter.ToInt32(_Data, bytepos));
                            bytepos += 4;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;

                case ParamTypes.BOOL:
                    {
                        List<Boolean> returnList = new List<Boolean>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            returnList.Add(BitConverter.ToBoolean(_Data, bytepos));
                            bytepos += 1;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;

                case ParamTypes.INT64:
                    {
                        List<Int64> returnList = new List<Int64>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            returnList.Add(BitConverter.ToInt64(_Data, bytepos));
                            bytepos += 8;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;
                case ParamTypes.UINT32:
                    {
                        List<UInt32> returnList = new List<UInt32>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            returnList.Add(BitConverter.ToUInt32(_Data, bytepos));
                            bytepos += 4;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;

                case ParamTypes.UINT64:
                    {
                        List<UInt64> returnList = new List<UInt64>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            returnList.Add(BitConverter.ToUInt64(_Data, bytepos));
                            bytepos += 8;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;

                case ParamTypes.INT16:
                    {
                        List<Int16> returnList = new List<Int16>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            returnList.Add(BitConverter.ToInt16(_Data, bytepos));
                            bytepos += 2;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;
                case ParamTypes.DECIMAL:
                    {
                        List<Decimal> returnList = new List<Decimal>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            Int32[] bits = new Int32[4];
                            for (Int32 i = 0; i < 4; i++) bits[i] = BitConverter.ToInt32(_Data, bytepos + (i * 4));
                            returnList.Add(new Decimal(bits));

                            bytepos += 16;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;
                case ParamTypes.UINT16:
                    {
                        List<UInt16> returnList = new List<UInt16>(listLength);
                        for (Int32 x = 0; x < listLength; x++)
                        {
                            returnList.Add(BitConverter.ToUInt16(_Data, bytepos));
                            bytepos += 2;
                        }
                        _PacketObjects.Add(returnList);
                    }
                    break;
                case ParamTypes.BYTE_PACKET:
                    break;
                case ParamTypes.UTF8_STRING:
                    break;
                case ParamTypes.COMPRESSED_BYTE_PACKET:
                    break;
                case ParamTypes.PACKET:
                    break;
                case ParamTypes.TIMESPAN:
                    break;
                case ParamTypes.DATETIME:
                    break;
                case ParamTypes.GUID:
                    break;
                default:
                    throw new PacketCorruptException("An internal unpacking error occured, Unknown internal data type present");
            }
            return bytepos;
        }

        private Int32 UnpackValue(Int32 bytepos)
        {
            switch ((ParamTypes)_Data[bytepos++])
            {
                case ParamTypes.DOUBLE:
                    {
                        _PacketObjects.Add(BitConverter.ToDouble(_Data, bytepos));
                        bytepos += sizeof(Double);
                    }
                    break;

                case ParamTypes.FLOAT:
                    {
                        _PacketObjects.Add(BitConverter.ToSingle(_Data, bytepos));
                        bytepos += sizeof(Single);
                    }
                    break;

                case ParamTypes.INT32:
                    {
                        _PacketObjects.Add(BitConverter.ToInt32(_Data, bytepos));
                        bytepos += sizeof(Int32);
                    }
                    break;

                case ParamTypes.BOOL:
                    {
                        _PacketObjects.Add(BitConverter.ToBoolean(_Data, bytepos));
                        bytepos += sizeof(Boolean);
                    }
                    break;

                case ParamTypes.INT64:
                    {
                        _PacketObjects.Add(BitConverter.ToInt64(_Data, bytepos));
                        bytepos += sizeof(Int64);
                    }
                    break;

                case ParamTypes.BYTE_PACKET:
                    {
                        Byte[] data = new Byte[BitConverter.ToInt32(_Data, bytepos)];
                        bytepos += 4;
                        Array.Copy(_Data, bytepos, data, 0, data.Length);
                        _PacketObjects.Add(data);
                        bytepos += data.Length;
                    }
                    break;

                case ParamTypes.PACKET:
                    {
                        Byte[] data = new Byte[BitConverter.ToInt32(_Data, bytepos)];
                        bytepos += 4;
                        Array.Copy(_Data, bytepos, data, 0, data.Length);
                        _PacketObjects.Add(FromByteArray(data));
                        bytepos += data.Length;
                    }
                    break;

                case ParamTypes.UINT32:
                    {
                        _PacketObjects.Add(BitConverter.ToUInt32(_Data, bytepos));
                        bytepos += sizeof(UInt32);
                    }
                    break;

                case ParamTypes.UINT64:
                    {
                        _PacketObjects.Add(BitConverter.ToUInt64(_Data, bytepos));
                        bytepos += sizeof(UInt64);
                    }
                    break;

                case ParamTypes.INT16:
                    {
                        _PacketObjects.Add(BitConverter.ToInt16(_Data, bytepos));
                        bytepos += sizeof(Int16);
                    }
                    break;

                case ParamTypes.UTF8_STRING:
                    {
                        Byte[] data = new Byte[BitConverter.ToInt32(_Data, bytepos)];
                        bytepos += 4;
                        Array.Copy(_Data, bytepos, data, 0, data.Length);
                        _PacketObjects.Add(Encoding.UTF8.GetString(data, 0, data.Length));
                        bytepos += data.Length;
                    }
                    break;

                case ParamTypes.DECIMAL:
                    {
                        Int32[] bits = new Int32[4];
                        for (Int32 i = 0; i < 4; i++) bits[i] = BitConverter.ToInt32(_Data, bytepos + (i * 4));
                        _PacketObjects.Add(new Decimal(bits));
                        bytepos += sizeof(Decimal);
                    }
                    break;

                case ParamTypes.COMPRESSED_BYTE_PACKET:
                    Byte[] data2 = new Byte[BitConverter.ToInt32(_Data, bytepos)];
                    bytepos += 4;
                    Array.Copy(_Data, bytepos, data2, 0, data2.Length);
                    _PacketObjects.Add(Uncompress(data2));
                    bytepos += data2.Length;
                    break;

                case ParamTypes.TIMESPAN:
                    {
                        _PacketObjects.Add(new TimeSpan(BitConverter.ToInt64(_Data, bytepos)));
                        bytepos += 8;
                    }
                    break;

                case ParamTypes.DATETIME:
                    {
                        _PacketObjects.Add(new DateTime(BitConverter.ToInt64(_Data, bytepos)));
                        bytepos += 8;
                    }
                    break;

                case ParamTypes.GUID:
                    {
                        Byte[] guidArray = new Byte[16];
                        Array.Copy(_Data, bytepos, guidArray, 0, 16);
                        _PacketObjects.Add(new Guid(guidArray));
                        bytepos += 16;
                    }
                    break;

                case ParamTypes.UINT16:
                    {
                        _PacketObjects.Add(BitConverter.ToUInt16(_Data, bytepos));
                        bytepos += sizeof(UInt16);
                    }
                    break;
                default:
                    throw new PacketCorruptException("An internal unpacking error occured, Unknown internal data type present");
            }
            return bytepos;
        }

        /// <summary>
        ///     Increases the size of the internal data array
        /// </summary>
        protected void ExpandDataArray()
        {
            try
            {
                _ReturnByteArray = null;
                Byte[] newData = new Byte[_Data.Length * 2];
                _Data.CopyTo(newData, 0);
                _Data = newData;
            }
            catch (OutOfMemoryException e)
            {
                throw new OutOfMemoryException("The internal packet data array failed to expand, Too much data allocated", e);
            }
        }

        /// <summary>
        ///     An enum containing supported types
        /// </summary>
        internal enum ParamTypes
        {
            FLOAT,
            DOUBLE,
            INT16,
            UINT16,
            INT32,
            UINT32,
            INT64,
            UINT64,
            BOOL,
            BYTE_PACKET,
            UTF8_STRING,
            COMPRESSED_BYTE_PACKET,
            DECIMAL,
            PACKET,
            TIMESPAN,
            DATETIME,
            GUID,
            UNKNOWN=1000
        };

        internal static ParamTypes DetermineParamType(Type t)
        {
            if (t == typeof(Boolean)) return ParamTypes.BOOL;
            if (t == typeof(Int16)) return ParamTypes.INT16;
            if (t == typeof(Int32)) return ParamTypes.INT32;
            if (t == typeof(Int64)) return ParamTypes.INT64;
            if (t == typeof(UInt16)) return ParamTypes.UINT16;
            if (t == typeof(UInt32)) return ParamTypes.UINT32;
            if (t == typeof(UInt64)) return ParamTypes.UINT64;
            if (t == typeof(Single)) return ParamTypes.FLOAT;
            if (t == typeof(Double)) return ParamTypes.DOUBLE;
            if (t == typeof(String)) return ParamTypes.UTF8_STRING;
            if (t == typeof(Byte[])) return ParamTypes.BYTE_PACKET;
            if (t == typeof(DateTime)) return ParamTypes.DATETIME;
            if (t == typeof(TimeSpan)) return ParamTypes.TIMESPAN;
            if (t == typeof(Guid)) return ParamTypes.GUID;
            if (t == typeof(Decimal)) return ParamTypes.DECIMAL;
            return ParamTypes.UNKNOWN;
        }

        /// <summary>
        ///     Converts a byte array to a packet
        /// </summary>
        /// <param name="data">the byte array to convery</param>
        /// <returns>Returns a packet build from a byte array</returns>
        public static Packet FromByteArray(Byte[] data)
        {
            Packet returnPacket = new Packet(BitConverter.ToUInt16(data, 10))
            {
                _ParamCount = BitConverter.ToUInt16(data, 4),
                _Data = new Byte[BitConverter.ToUInt32(data, 6) - 12]
            };
            returnPacket._DataPos = (UInt32)returnPacket._Data.Length;
            Array.Copy(data, 12, returnPacket._Data, 0, returnPacket._Data.Length);
            returnPacket.UpdateObjects();
            return returnPacket;
        }

        /// <summary>
        /// Reads a packet from the provided stream
        /// </summary>
        /// <param name="data">The stream from which the packet should be sourced</param>
        /// <returns></returns>
        public static Packet FromStream(Stream data)
        {
            const Int32 PACKET_HEADER_LENGTH = 12;
            Byte[] packetHeader = new Byte[PACKET_HEADER_LENGTH];
            data.Read(packetHeader, 0, PACKET_HEADER_LENGTH);

            if (!TestForPacketHeader(packetHeader)) throw new NotAPacketException();

            UInt32 remainingPacketLength = BitConverter.ToUInt32(packetHeader, 6);
            Byte[] packetData = new Byte[PACKET_HEADER_LENGTH + remainingPacketLength];
            data.Read(packetData, PACKET_HEADER_LENGTH, (Int32)remainingPacketLength);
            Array.Copy(packetHeader, packetData, PACKET_HEADER_LENGTH);

            return FromByteArray(packetData);
        }

        public static Byte[] Uncompress(Byte[] bytes)
        {
            using (ICSharpCode.SharpZipLib.GZip.GZipInputStream ds = new ICSharpCode.SharpZipLib.GZip.GZipInputStream(new MemoryStream(bytes)))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    ds.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        public static Byte[] Compress(Byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (ICSharpCode.SharpZipLib.GZip.GZipOutputStream ds = new ICSharpCode.SharpZipLib.GZip.GZipOutputStream(ms))
                {
                    ds.Write(bytes, 0, bytes.Length);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Returns whether the packet header detected in the array has the correct packet start byte marks
        /// </summary>
        /// <param name="data">The array to test</param>
        /// <returns>True if the array has the correct byte start marks else false</returns>
        private static Boolean TestForPacketHeader(IList<Byte> data)
        {
            return !PacketStart.Where((t, x) => data[x] != t).Any();
        }
    }
}