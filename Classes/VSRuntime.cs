using UnityEngine;
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using System.Runtime.InteropServices;
using Unity.Entities;

namespace MaxyGames.UNode {
	#region DATA
	internal struct ExecutionEvent {
		public int graphID;
		public int nodeID;
		public byte type;   // 0 = Flow, 1 = Value
	}
	#endregion

	#region RING BUFFER

	internal unsafe struct NativeRingBuffer : IDisposable {
		[NativeDisableUnsafePtrRestriction]
		public ExecutionEvent* buffer;

		public int capacity;

		[NativeDisableUnsafePtrRestriction]
		public int* writeIndex;

		private Allocator allocator;

		public bool IsCreated => buffer != null;

		public static NativeRingBuffer Create(int capacity, Allocator allocator) {
			var buffer = (ExecutionEvent*)UnsafeUtility.Malloc(
				sizeof(ExecutionEvent) * capacity,
				16,
				allocator);

			var indexPtr = (int*)UnsafeUtility.Malloc(sizeof(int), 4, allocator);
			*indexPtr = 0;

			return new NativeRingBuffer {
				buffer = buffer,
				capacity = capacity,
				writeIndex = indexPtr,
				allocator = allocator
			};
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Reset() {
			if(writeIndex != null)
				*writeIndex = 0;
		}

		public void Dispose() {
			if(buffer != null) {
				UnsafeUtility.Free(buffer, allocator);
				buffer = null;
			}

			if(writeIndex != null) {
				UnsafeUtility.Free(writeIndex, allocator);
				writeIndex = null;
			}

			capacity = 0;
		}
	}

	#endregion

	#region RUNTIME
	public static unsafe class VSRuntime {
		static NativeRingBuffer[] buffers;
		static int writeIdx;
		static int readIdx;

		internal static NativeRingBuffer* WritePtr;

		static int requestedCapacity = -1;

		public static void Init(int capacity) {
			buffers = new NativeRingBuffer[2];

			for(int i = 0; i < 2; i++) {
				buffers[i] = NativeRingBuffer.Create(capacity, Allocator.Persistent);
			}

			writeIdx = 0;
			readIdx = 1;

			UpdatePtr();
		}

		static void UpdatePtr() {
			fixed(NativeRingBuffer* ptr = &buffers[writeIdx]) {
				WritePtr = ptr;
			}
		}

		// ========================
		// WRITE (BURST SAFE)
		// ========================

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void WriteEvent(ExecutionEvent evt) {
			var rb = *WritePtr;

			if(rb.buffer == null)
				return;

			int index = Interlocked.Increment(ref *rb.writeIndex) - 1;

			rb.buffer[index % rb.capacity] = evt;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Flow(int graphID, int nodeID) {
			WriteEvent(new ExecutionEvent {
				graphID = graphID,
				nodeID = nodeID,
				type = 0
			});
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T Value<T>(T value, int graphID, int nodeID) {
			WriteEvent(new ExecutionEvent {
				graphID = graphID,
				nodeID = nodeID,
				type = 1,
			});

			return value;
		}

		// ========================
		// READ ACCESS
		// ========================

		internal static NativeRingBuffer GetReadBuffer() {
			return buffers[readIdx];
		}

		// ========================
		// SWAP + AUTO RESIZE
		// ========================

		public static void SwapBuffers() {
			var writeBuffer = buffers[writeIdx];
			int written = *writeBuffer.writeIndex;

			// detect overflow → request resize
			if(written > writeBuffer.capacity) {
				int newCap = math.ceilpow2(written);
				requestedCapacity = math.max(newCap, writeBuffer.capacity * 2);
			}

			// apply resize safely
			if(requestedCapacity > 0) {
				Resize(requestedCapacity);
				requestedCapacity = -1;
				return;
			}

			// normal swap
			int tmp = writeIdx;
			writeIdx = readIdx;
			readIdx = tmp;

			buffers[writeIdx].Reset();
			UpdatePtr();
		}

		static void Resize(int newCapacity) {
#if UNODE_DEV || UNODE_DEBUG
			Debug.Log($"[VSRuntime] Resizing buffer → {newCapacity}");
#endif

			for(int i = 0; i < buffers.Length; i++) {
				buffers[i].Dispose();
			}

			buffers = new NativeRingBuffer[2];

			for(int i = 0; i < 2; i++) {
				buffers[i] = NativeRingBuffer.Create(newCapacity, Allocator.Persistent);
			}

			writeIdx = 0;
			readIdx = 1;

			UpdatePtr();

			VSReader.Reset();
		}

		public static void Dispose() {
			if(buffers == null)
				return;

			for(int i = 0; i < buffers.Length; i++) {
				buffers[i].Dispose();
			}

			buffers = null;
		}
	}

	#endregion

	#region READER
	internal static unsafe class VSReader {
		static int lastRead;

		public static void Reset() {
			lastRead = 0;
		}

		public static void ReadAll(Action<ExecutionEvent> callback) {
			var rb = VSRuntime.GetReadBuffer();

			if(!rb.IsCreated)
				return;

			int current = *rb.writeIndex;

			int start = math.max(lastRead, current - rb.capacity);

			for(int i = start; i < current; i++) {
				var evt = rb.buffer[i % rb.capacity];
				callback(evt);
			}

			lastRead = current;
		}
	}
	#endregion
}