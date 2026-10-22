package com.markerml2.ble;

import android.bluetooth.*;
import com.velorexe.unityandroidble.UnityAndroidBLE;
import java.lang.reflect.Field;
import java.util.Map;
import java.util.UUID;

/** Companion for Velorexe 0.0.6, commit 55d8c12e155cc45bc26cea120d5e8c1e5f231b9f.
 * Uses the plugin's existing connection and callback; does not create a second connection.
 * Reflection accesses a private field of THAT plugin, never Android hidden APIs.
 * Calls must be serialized by the C# caller until DescriptorWrite/ReadFromCharacteristic.
 */
@SuppressWarnings("deprecation")
public final class CosinussGatt {
    private static final UUID CCCD = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");
    private static BluetoothGatt gatt(String address) throws Exception {
        Field field = UnityAndroidBLE.class.getDeclaredField("mLeGattServers");
        field.setAccessible(true);
        Map<?,?> map = (Map<?,?>)field.get(null);
        if (map != null) for (Object value : map.values()) {
            BluetoothGatt g = (BluetoothGatt)value;
            if (g != null && g.getDevice().getAddress().equalsIgnoreCase(address)) return g;
        }
        throw new IllegalStateException("Keine aktive Velorexe-GATT-Verbindung");
    }
    private static BluetoothGattCharacteristic characteristic(BluetoothGatt g,String service,String characteristic) {
        BluetoothGattService s = g.getService(UUID.fromString(service));
        if (s == null) throw new IllegalArgumentException("Service fehlt: " + service);
        BluetoothGattCharacteristic c = s.getCharacteristic(UUID.fromString(characteristic));
        if (c == null) throw new IllegalArgumentException("Characteristic fehlt: " + characteristic);
        return c;
    }
    public static int properties(String address,String service,String uuid) throws Exception {
        return characteristic(gatt(address),service,uuid).getProperties();
    }
    // preferredMode: 0=auto (notify first), 1=notify preferred, 2=indicate preferred.
    // Returns selected mode; actual data reception remains the confirmation of flow.
    public static int subscribe(String address,String service,String uuid,int preferredMode) throws Exception {
        BluetoothGatt g = gatt(address);
        BluetoothGattCharacteristic c = characteristic(g,service,uuid);
        int p = c.getProperties(), mode;
        if (preferredMode == 2 && (p & BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0) mode=2;
        else if ((p & BluetoothGattCharacteristic.PROPERTY_NOTIFY) != 0) mode=1;
        else if ((p & BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0) mode=2;
        else throw new IllegalStateException("Weder NOTIFY noch INDICATE vorhanden");
        BluetoothGattDescriptor d = c.getDescriptor(CCCD);
        if (d == null) throw new IllegalStateException("CCCD fehlt");
        if (!g.setCharacteristicNotification(c,true)) throw new IllegalStateException("Lokale Registrierung abgelehnt");
        d.setValue(mode == 2 ? BluetoothGattDescriptor.ENABLE_INDICATION_VALUE : BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
        if (!g.writeDescriptor(d)) {
            g.setCharacteristicNotification(c,false);
            throw new IllegalStateException("CCCD-Auftrag abgelehnt");
        }
        return mode;
    }
    public static boolean read(String address,String service,String uuid) throws Exception {
        BluetoothGatt g = gatt(address);
        BluetoothGattCharacteristic c = characteristic(g,service,uuid);
        if ((c.getProperties() & BluetoothGattCharacteristic.PROPERTY_READ) == 0) return false;
        return g.readCharacteristic(c);
    }
}
