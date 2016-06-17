﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace SniperUsbDmx
{
    
    public static class DmxController<T> where T : DMXProUSB, new()
    {
        private static ThreadSafeList<Effect> effectQueue = new ThreadSafeList<Effect>();
        private static Thread runningThread;
        private static bool done;
        private static T dmxDevice;
        private readonly static int busLength = 512;

        static DmxController()
        {
            dmxDevice = new T();
            done = false;
            for (int i = 0; i < busLength; i++)
            {
                dmxDevice.SetDmxValue(i, 0, Guid.Empty, 1);
            }            
            runningThread = new Thread(new ThreadStart(Run));
            runningThread.IsBackground = true;
            runningThread.Start();
            dmxDevice.StateChanged += new StateChangedEventHandler(DMXProUSB_StateChanged);
        }

        public static void QueueEffect(Effect e)
        {
            //create a lighting queue for this effect
            dmxDevice.CreateQueue(e.Queue, e.Priority);
            effectQueue.Add(e);
        }

        static void DMXProUSB_StateChanged(object sender, StateChangedEventArgs e)
        {
            if (StateChanged != null)
            {
                StateChanged(sender, e);
            }
        }

        public static Effect EaseDmxValue(Guid queue, int channel, int priority, byte endValue, int duration, EasingType typeIn, EasingType typeOut, EasingExtents extents)
        {
            byte startValue = dmxDevice.GetDmxValue(channel);
            Effect handle = new Effect(queue, channel, priority, startValue, endValue, duration, typeIn, typeOut, extents);
            QueueEffect(handle);
            handle.Start();
            return handle;
        }

        public static Effect EaseDmxValue(Guid queue, int channel, int priority, byte startValue, byte endValue, int duration, EasingType typeIn, EasingType typeOut, EasingExtents extents)
        {
            Effect handle = new Effect(queue, channel, priority, startValue, endValue, duration, typeIn, typeOut, extents);
            QueueEffect(handle);
            handle.Start();
            return handle;
        }

        public static Effect EaseDmxValue(Guid queue, int channel, int priority, byte startValue, byte endValue, int duration, EasingType typeIn, EasingType typeOut, EasingExtents extents, DateTime when)
        {
            Effect handle = new Effect(queue, channel, priority, startValue, endValue, duration, typeIn, typeOut, extents);
            QueueEffect(handle);
            handle.StartIn((int)(when - DateTime.Now).TotalMilliseconds);            
            return handle;
        }

        public static void SetDmxValue(Guid queue, int channel, int priority, byte value, DateTime when)
        {
            Effect handle = new Effect(queue, channel, priority, null, value, 0, EasingType.Linear, EasingType.Linear, EasingExtents.EaseInOut);
            QueueEffect(handle);
            handle.StartIn((int)(when - DateTime.Now).TotalMilliseconds);          
        }

        public static void SetDmxValue(Guid queue, int channel, int priority, byte value)
        {
            SetDmxValue(queue, channel, priority, value, DateTime.Now);
        }

        public static void SetDmxValue(Guid queue, int channel, int priority, byte value, int revertInDuration)
        {
            SetDmxValue(queue, channel, priority, value);
            if (revertInDuration != 0)
            {
                SetDmxValue(queue, channel, priority, 0, DateTime.Now.AddMilliseconds(revertInDuration));
            }
        }

        public static Effect PulseDmxValue(Guid queue, int channel, int priority, byte startValue, byte endValue, int duration, EasingType typeIn, EasingType typeOut, EasingExtents extents)
        {
            var handle = new Pulse(queue, channel, priority, startValue, endValue, duration, typeIn, typeOut, extents);
            QueueEffect(handle);
            handle.Start();
            return handle;
        }

        public static byte GetDmxValue(int channel)
        {
            return dmxDevice.GetDmxValue(channel);
        }

        private static void Run()
        {
            Queue<Effect> toRemove = new Queue<Effect>();
            while (!done)
            {
                int activeEffectsCount = effectQueue.Count;
                foreach (Effect e in effectQueue)
                {
                    DateTime moment = DateTime.Now;
                    if (e.FromTimestamp <= moment) //don't check the upper bound otherwise if it's an instant effect, it's already passed by the time this is evaluated
                    {
                        byte value = e.GetCurrentValue();
                        Guid queue = e.Queue;
                        int channel = e.Channel;
                        int priority = e.Priority; //it's our pie-ority
                        dmxDevice.SetDmxValue(channel, value, queue, priority);
                    }
                    if (moment > e.ToTimestamp)
                    {
                        toRemove.Enqueue(e);
                    }
                }
                while(toRemove.Count > 0)
                {
                    Effect finishedEffect = toRemove.Dequeue();
                    effectQueue.Remove(finishedEffect);
                }
                var activeQueues = effectQueue.Select(x => x.Queue).Distinct();
                var currentQueues = dmxDevice.GetCurrentQueueIds();
                var inactiveQueues = currentQueues.Except(activeQueues);
                foreach(var queue in inactiveQueues)
                {
                    if (queue != Guid.Empty) //never delete empty guid queue, used for test UI
                    {
                        dmxDevice.DeleteQueue(queue);
                    }
                }
                Thread.Sleep(5);
            }
        }

        public static void Dispose()
        {
            done = true;
            Thread.Sleep(500);
            if (runningThread.ThreadState == ThreadState.Running)
            {
                runningThread.Abort();
            }
            runningThread = null;

            dmxDevice.Dispose();
        }

        public static byte?[] GetCurrentValues()
        {
            return dmxDevice.GetCurrentBuffer();
        }

        public static bool Start()
        {
            return dmxDevice.start();
        }

        public static bool Connected { get { return dmxDevice.Connected; } }
        public static void SetLimits(DmxLimits newLimits)
        {
            dmxDevice.setLimits(newLimits);
        }

        public static event StateChangedEventHandler StateChanged;


        public static void Stop()
        {
            foreach(Effect e in effectQueue)
            {
                e.Stop();
            }
            dmxDevice.stop();
        }
    }
}
