# 体rotation (Body Rotation)

VRChat メニューのラジアルでポーズ全体の回転を調整する機能です。

## 概要

ポーズクリップに回転を**焼き込まず**、アーマチュア自身の `localPosition` / `localRotation` を
実行時に書いて合成します。これにより、以下の利点があります。

- 1 ポーズあたりのクリップ数が増えない
- 回転をポーズに固定せず、後から自由に調整できる
- **Unity のプレイモードでも VRChat でも同じように動く**

> 以前のバージョンは `VRCRotationConstraint` / `VRCPositionConstraint` でアーマチュアを
> 追従させていました。VRChat の制約コンポーネントは VRChat のクライアントが解くもので、
> Unity のプレイモードでは一切動きません。そのため「プレイモードでは鎖だけ回って
> 体が付いてこない」「アップロードするまで正しいか分からない」という症状になっていました。
> 現在は制約を使わない方式に置き換わっています。

## 有効化

`AvatarPoseLibrary` コンポーネントのインスペクターで、**Body Rotation パラメーターを有効化**
チェックボックスをオンにします。

<details>
<summary><b>詳しく見る</b></summary>

- `AvatarPoseData.enableBodyRotationParam` を true にすると、パラメーターが生成されます。
- `enableBodyRotationParam` が true でも、`enableLocomotionAnimator` が false の場合は
  回転レイヤーは生成されません（`UseBodyRotationParam` が false になるため）。
</details>

## 実行時構造

階層は一切変わりません。変わるのはアーマチュアの Transform の値だけです。

```
AvatarRoot
└─ Armature        ... この localPosition / localRotation を FX レイヤーが書く
   └─ Hips         ... 回転の支点
```

- **Armature** の親子関係・ボーンパスは変えません。位置制約や付け替えは行いません。
- **高さスライダー**は従来どおりポーズ側のクリップ（Root Transform Position Y）が持ちます。
  クリップに `MotionT` が含まれる場合はアーマチュア基準で足されるため、
  体を横倒しにすると高さの向きも一緒に傾きます（仕様）。
- 回転中は IK を切るレイヤー（`AnimPoseBodyRotateIK`）が働き、クリップ側の姿勢をそのまま出します。

## レイヤー構成

FX アニメーターへ次の 3 本を追加します。ポーズ側の FX コントローラーとは
**別コントローラー**としてマージします（Write Defaults を必ず false にする必要があるため）。

| レイヤー名 | 役割 |
|---|---|
| `AnimPoseBodyRotateRig_{Guid}` | アーマチュアの姿勢を書く。軸ごとの `Simple1D` を X → Y → Z に入れ子にしたブレンドツリー（Unity の公開 API に 3 パラメーターのブレンドツリーは無いため） |
| `AnimPoseBodyRotateReset_{Guid}` | `AnimPoseBodyRotateReset` で X / Y / Z を無回転へ戻し、トリガー自身も 0 に戻す |
| `AnimPoseBodyRotateIK_{Guid}` | 3 軸すべてが無回転から外れている間だけ IK を切る（`RotationEpsilon = 0.002`） |

- マージ時は `matchAvatarWriteDefaults = false` で、Write Defaults を触らせません。
- IK を戻すのは **3 軸すべてが無回転へ戻ったときだけ**です。1 軸でも傾いている間に戻すと、
  IK の解が競合して体が一瞬別の向きへ飛びます（90 度を超えたあたりで顕著）。

## 角度範囲

| 軸 | 最小角 | 最大角 | 無回転位置 |
|---|---|---|---|
| X | 0 ° | 360 ° | 0 %（左端） |
| Y | -180 ° | +180 ° | 50 %（中央） |
| Z | 0 ° | 360 ° | 0 %（左端） |

### 注意点

- **支点は Hips です。** X / Z を 360 ° まで回すと、途中で頭や足が床を突き抜けます。支点自体は変わりません。
- **X / Z は 0 ° 始まりの一方向**なので、逆方向へは 360 ° 回り込んで到達します。左右対称に振りたい場合は `MinAngle` を -360f にしてください。
- **Y は中央（50%）が無回転**で、左右対称に振れます。

## パラメーター

| パラメーター名 | 意味 | 範囲 |
|---|---|---|
| `AnimPoseBodyRotateX` | X軸回転 | 0 .. 1（0°=0°, 1°=360°） |
| `AnimPoseBodyRotateY` | Y軸回転 | 0 .. 1（0%=−180°, 1=180°） |
| `AnimPoseBodyRotateZ` | Z軸回転 | 0 .. 1（0°=0°, 1°=360°） |
| `AnimPoseBodyRotateReset` | リセット | 0=無回転, 1=リセット |

## 位置変換とキャンバス

メニューでラジアルを直接動かせるのは **Y 軸だけ**です。X / Z は UI のスライダーや数値入力からしか変更できません。

これは、所有者に紐づくキャンバスの RectTransform を指定しているためです。キャンバスには以下のような制約があります。

- 所有者が見ている全アクターで見た目が揃う
- ローカル空間側にスライダーを投影できない（キャンバスはスクリーン空間）

## 実装上の注意

- **制約コンポーネントは使わない**  
  Unity の `RotationConstraint` を残したままアップロードすると PC 以外のプラットフォームで失敗します。
  VRChat の制約コンポーネントは Unity のプレイモードで動かないため、動作確認ができません。
  この機能はどちらも使わず、Transform のカーブだけで完結します。

- **アーマチュアを鎖の配下へ付け替えない**  
  付け替えると Hips 以下のボーンパスが変わり、既存のポーズクリップが参照できなくなります。
  さらに VRCFury の `ArmatureLink` はアップロード時のパス表からボーンを引くため、
  パス変更でビルドが失敗します。

- **クリップ数はポーズ数に依存しない**  
  3 軸は同時に掛け合わさるため、必要なクリップは `Steps^3` 本です。  
  `BodyRotationSteps = 5`（変更不可推奨）の場合、**全ポーズ共通で 125 本**です。

- **アーマチュアの初期姿勢はビルド時に焼き込まれる**  
  回転は「無回転時の姿勢」を基準に計算します。ビルド後に別ツールがアーマチュアの
  位置・回転を動かす構成（特殊な Armature 差し替えなど）では、その値が基準になります。

## トラブルシューティング

| 症状 | 原因 | 対処 |
|---|---|---|
| 回転が効かない | `Enable Locomotion Animator` が false | ポーズの `Enable Locomotion Animator` を true にする（コンソールに警告が出ます） |
| 回転が効かない | アーマチュア / Hips が見つからない | コンソールの `Body rotation is enabled but cannot be built.` を確認する |
| 顔や足が床を突き抜ける | X / Z を 360° まで回している | `MaxAngle` を下げる、支点が Hips であることを受け入れる |
| メニューに項目が出ない | `enableBodyRotationParam` が false、または `parameterOverride` の重複 | インスペクターのチェックを確認する |

## 既知の制限

- **高さスライダーはアーマチュア基準**  
  高さはポーズクリップ側（Root Transform Position Y）が持ちます。`MotionT` を含むクリップでは
  アーマチュアのローカル空間で足されるため、体を横倒しにすると高さの向きも傾きます。
- **X / Z は一方向**  
  0 ° 始まりなので、逆方向へは 360 ° 回り込んで到達します。左右対称に振るには `MinAngle` を変更してください。
- **支点は Hips 固定**  
  X / Z を 360 ° まで回すと頭や足が床を突き抜けます。支点は変更できません。
- **複数ライブラリを同時に使う場合**  
  どのライブラリも同じアーマチュアの Transform を書くため、後から評価されたレイヤーが優先されます。
- **アップロードしないと確定しない部分がある**  
  IK・同期・パラメーター保存の挙動は VRChat 上でのみ確定します。回転そのものはプレイモードで確認できます。

## 関連設定

```csharp
// ConstVariables.cs
public const int BodyRotationSteps = 5;                 // ラジアル分解能（0..1 を 5 段に分割）
public const float BodyRotationMinAngleX = 0f;            // X軸 最小角
public const float BodyRotationMaxAngleX = 360f;          // X軸 最大角
public const float BodyRotationMinAngleY = -180f;         // Y軸 最小角
public const float BodyRotationMaxAngleY = 180f;          // Y軸 最大角
public const float BodyRotationMinAngleZ = 0f;            // Z軸 最小角
public const float BodyRotationMaxAngleZ = 360f;          // Z軸 最大角
public const string BodyRotationRigLayerPrefix = "AnimPoseBodyRotateRig"; // レイヤー名
```

## 開発者向けメモ

- パラメーター名は `BodyRotationAxis` 構造体で管理されています。軸ごとに `AxisName` / `ParameterPrefix` / `Axis` / `MinAngle` / `MaxAngle` をまとめており、無回転値（`NeutralValue`）は `MinAngle` / `MaxAngle` から導出されます。
- 支点と基準姿勢の計算は `BodyRotationArmatureSolver` が担当します（`Logic/` フォルダー）。
  アバタールートのローカル空間で
  `localPosition = h + R * (a - h)` / `localRotation = R * q`
  を解くだけで、Hips を支点にした剛体回転になります（`a` = 無回転時の localPosition、`q` = 無回転時の localRotation、`h` = Hips の位置、`R` = 加えたい回転）。
- レイヤーとクリップの生成は `BodyRotationRigBuilder` が担当します。
  軸ごとの `Simple1D` ブレンドツリーを X → Y → Z に入れ子にし、合わせて `Steps^3` 個の格子点を置きます（`BlendTreeType.FreeformCartesian3D` / `blendParameterZ` は Unity の公開 API に存在しないため）。
- パラメーター名を自動生成するため、`ParameterBuilder.cs` が `AnimPoseBodyRotateX/Y/Z` 等の文字列を生成します。
